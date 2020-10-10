// Copyright 2018 Fabulous contributors. See LICENSE.md for license.

// F# PortaCode command processing (e.g. used by Fabulous.Cli)

module FSharp.Compiler.PortaCode.ProcessCommandLine

open FSharp.Compiler.PortaCode.CodeModel
open FSharp.Compiler.PortaCode.Interpreter
open FSharp.Compiler.PortaCode.FromCompilerService
open System
open System.Reflection
open System.Collections.Generic
open System.IO
open FSharp.Compiler.SourceCodeServices
open FSharp.Compiler.Text
open System.Net
open System.Text

let checker = FSharpChecker.Create(keepAssemblyContents = true)

let ProcessCommandLine (argv: string[]) =
    let mutable fsproj = None
    let mutable dump = false
    let mutable livecheck = false
    let mutable dyntypes = false
    let mutable watch = true
    let mutable useEditFiles = false
    let mutable writeinfo = true
    let mutable webhook = None
    let mutable otherFlags = []
    let mutable msbuildArgs = []
    let defaultUrl = "http://localhost:9867/update"
    let fsharpArgs = 
        let mutable haveDashes = false

        [| for arg in argv do 
                let arg = arg.Trim()
                if arg.StartsWith("@") then 
                    for line in File.ReadAllLines(arg.[1..]) do 
                        let line = line.Trim()
                        if not (String.IsNullOrWhiteSpace(line)) then
                            yield line
                elif arg.EndsWith(".fsproj") then 
                    fsproj <- Some arg
                elif arg = "--" then haveDashes <- true
                elif arg.StartsWith "--projarg:" then msbuildArgs <- msbuildArgs @ [ arg.["----projarg:".Length ..]] 
                elif arg.StartsWith "--define:" then otherFlags <- otherFlags @ [ arg ]
                elif arg = "--once" then watch <- false
                elif arg = "--dump" then dump <- true
                elif arg = "--livecheck" then 
                    dyntypes <- true
                    livecheck <- true
                    writeinfo <- true
                    useEditFiles <- true
                elif arg = "--enablelivechecks" then 
                    livecheck <- true
                elif arg = "--useeditfles" then 
                    useEditFiles <- true
                elif arg = "--dyntypes" then 
                    dyntypes <- true
                elif arg = "--writeinfo" then 
                    writeinfo <- true
                elif arg.StartsWith "--send:" then webhook  <- Some arg.["--send:".Length ..]
                elif arg = "--send" then webhook  <- Some defaultUrl
                elif arg = "--version" then 
                   printfn ""
                   printfn "*** NOTE: if sending the code to a device the versions of CodeModel.fs and Interpreter.fs on the device must match ***"
                   printfn ""
                   printfn "CLI tool assembly version: %A" (System.Reflection.Assembly.GetExecutingAssembly().GetName().Version)
                   printfn "CLI tool name: %s" (System.Reflection.Assembly.GetExecutingAssembly().GetName().Name)
                   printfn ""
                elif arg = "--help" then 
                   printfn "Command line tool for watching and interpreting F# projects"
                   printfn ""
                   printfn "Usage: <tool> arg .. arg [-- <other-args>]"
                   printfn "       <tool> @args.rsp  [-- <other-args>]"
                   printfn "       <tool> ... Project.fsproj ... [-- <other-args>]"
                   printfn ""
                   printfn "The default source is a single project file in the current directory."
                   printfn "The default output is a JSON dump of the PortaCode."
                   printfn ""
                   printfn "Arguments:"
                   printfn "   --once            Don't enter watch mode (default: watch the source files of the project for changes)"
                   printfn "   --send:<url>      Send the JSON-encoded contents of the PortaCode to the webhook"
                   printfn "   --send            Equivalent to --send:%s" defaultUrl
                   printfn "   --projarg:arg  An MSBuild argument e.g. /p:Configuration=Release"
                   printfn "   --dump            Dump the contents to console after each update"
                   printfn "   --livecheck       Only evaluate those with a LiveCheck attribute"
                   printfn "                     This uses on-demand execution semantics for top-level declarations"
                   printfn "                     Also write an info file based on results of evaluation."
                   printfn "                     Also watch for .fsharp/foo.fsx.edit files and use the contents of those in preference to the source file"
                   printfn "   --dyntypes      Dynamically compile and load so full .NET types exist"
                   printfn "   <other-args>      All other args are assumed to be extra F# command line arguments, e.g. --define:FOO"
                   exit 1
                else yield arg  |]

    if fsharpArgs.Length = 0 && fsproj.IsNone then 
        match Seq.toList (Directory.EnumerateFiles(Environment.CurrentDirectory, "*.fsproj")) with 
        | [ ] -> 
            failwithf "no project file found, no compilation arguments given and no project file found in \"%s\"" Environment.CurrentDirectory 
        | [ file ] -> 
            printfn "fslive: using implicit project file '%s'" file
            fsproj <- Some file
        | file1 :: file2 :: _ -> 
            failwithf "multiple project files found, e.g. %s and %s" file1 file2 

    let editDirAndFile (fileName: string) =
        assert useEditFiles
        let infoDir = Path.Combine(Path.GetDirectoryName fileName,".fsharp")
        let editFile = Path.Combine(infoDir,Path.GetFileName fileName + ".edit")
        if not (Directory.Exists infoDir) then 
            Directory.CreateDirectory infoDir |> ignore
        infoDir, editFile

    let readFile (fileName: string) = 
        if useEditFiles && watch then 
            let infoDir, editFile = editDirAndFile fileName
            let preferEditFile =
                try 
                    Directory.Exists infoDir && File.Exists editFile && File.Exists fileName && File.GetLastWriteTime(editFile) > File.GetLastWriteTime(fileName)
                with _ -> 
                    false
            if preferEditFile then 
                printfn "*** preferring %s to %s ***" editFile fileName
                File.ReadAllText editFile
            else
                File.ReadAllText fileName
        else
            File.ReadAllText fileName

    let options = 
        match fsproj with 
        | Some fsprojFile -> 
            if fsharpArgs.Length > 1 then failwith "can't give both project file and compilation arguments"
            match FSharpDaemon.ProjectCracker.load (new System.Collections.Concurrent.ConcurrentDictionary<_,_>()) fsprojFile msbuildArgs with 
            | Ok (options, sourceFiles, _log) -> 
                let options = { options with SourceFiles = Array.ofList sourceFiles }
                let sourceFilesSet = Set.ofList sourceFiles
                let options = { options with OtherOptions = options.OtherOptions |> Array.filter (fun s -> not (sourceFilesSet.Contains(s))) }
                Result.Ok options
            | Error err -> 
                failwithf "Couldn't parse project file: %A" err
            
        | None -> 
            let sourceFiles, otherFlags2 = fsharpArgs |> Array.partition (fun arg -> arg.EndsWith(".fs") || arg.EndsWith(".fsi") || arg.EndsWith(".fsx"))
            let otherFlags =[| yield! otherFlags; yield! otherFlags2 |]
            let sourceFiles = sourceFiles |> Array.map Path.GetFullPath 
            printfn "CurrentDirectory = %s" Environment.CurrentDirectory
        
            match sourceFiles with 
            | [| script |] when script.EndsWith(".fsx") ->
                let text = readFile script
                let otherFlags = Array.append otherFlags [| "--targetprofile:netcore"; |]
                let options, errors = checker.GetProjectOptionsFromScript(script, SourceText.ofString text, otherFlags=otherFlags, assumeDotNetFramework=false) |> Async.RunSynchronously
                let options = { options with OtherOptions = Array.append options.OtherOptions [| "--target:library" |] }
                if errors.Length > 0 then 
                    for error in errors do 
                        printfn "%s" (error.ToString())
                    Result.Error ()
                else                                
                    //let options = { options with SourceFiles = sourceFiles }
                    Result.Ok options
            | _ -> 
                let options = checker.GetProjectOptionsFromCommandLineArgs("tmp.fsproj", otherFlags)
                let options = { options with SourceFiles = sourceFiles }
                Result.Ok options

    match options with 
    | Result.Error () -> 
        printfn "fslive: error processing project options or script" 
        -1
    | Result.Ok options ->
    let options = { options with OtherOptions = Array.append options.OtherOptions (Array.ofList otherFlags) }
    //printfn "options = %A" options

    let rec checkFile count sourceFile =         
        try 
            let parseResults, checkResults = checker.ParseAndCheckFileInProject(sourceFile, 0, SourceText.ofString (readFile sourceFile), options) |> Async.RunSynchronously  
            match checkResults with 
            | FSharpCheckFileAnswer.Aborted -> 
                failwith "unexpected aborted"
                Result.Error (parseResults.ParseTree, None, None, None)

            | FSharpCheckFileAnswer.Succeeded res -> 
                let mutable hasErrors = false
                for error in res.Errors do 
                    printfn "%s" (error.ToString())
                    if error.Severity = FSharpErrorSeverity.Error then 
                        hasErrors <- true

                if hasErrors then 
                    Result.Error (parseResults.ParseTree, None, Some res.Errors, res.ImplementationFile)
                else
                    Result.Ok (parseResults.ParseTree, res.ImplementationFile)
        with 
        | :? System.IO.IOException when count = 0 -> 
            System.Threading.Thread.Sleep 500
            checkFile 1 sourceFile
        | exn -> 
            printfn "%s" (exn.ToString())
            Result.Error (None, Some exn, None, None)

    let keepRanges = not dump
    let convFile (i: FSharpImplementationFileContents) =         
        //(i.QualifiedName, i.FileName
        i.FileName, { Code = Convert(keepRanges).ConvertDecls i.Declarations }

    let checkFiles files =             
        let rec loop rest acc = 
            match rest with 
            | file :: rest -> 
                match checkFile 0 (Path.GetFullPath(file)) with 

                // Note, if livecheck are on, we continue on regardless of errors
                | Result.Error iopt when not livecheck -> 
                    printfn "fslive: ERRORS for %s" file
                    Result.Error iopt

                | Result.Error ((_, _, _, None) as info) -> Result.Error info
                | Result.Ok (_, None) -> Result.Error (None, None, None, None)
                | Result.Error (parseTree, _, _, Some implFile)
                | Result.Ok (parseTree, Some implFile) ->
                    printfn "fslive: GOT PortaCode for %s" file
                    loop rest ((parseTree, implFile) :: acc)
            | [] -> Result.Ok (List.rev acc)
        loop (List.ofArray files) []

    let jsonFiles (impls: FSharpImplementationFileContents[]) =         
        let data = Array.map convFile impls
        let json = Newtonsoft.Json.JsonConvert.SerializeObject(data)
        json

    let sendToWebHook (hook: string) fileContents = 
        try 
            let json = jsonFiles (Array.ofList fileContents)
            printfn "fslive: GOT JSON, length = %d" json.Length
            use webClient = new WebClient(Encoding = Encoding.UTF8)
            printfn "fslive: SENDING TO WEBHOOK... " // : <<<%s>>>... --> %s" json.[0 .. min (json.Length - 1) 100] hook
            let resp = webClient.UploadString (hook,"Put",json)
            printfn "fslive: RESP FROM WEBHOOK: %s" resp
        with err -> 
            printfn "fslive: ERROR SENDING TO WEBHOOK: %A" (err.ToString())

    let emitInfoFile (sourceFile: string) lines = 
        let infoDir = Path.Combine(Path.GetDirectoryName(sourceFile), ".fsharp")
        let infoFile = Path.Combine(infoDir, Path.GetFileName(sourceFile) + ".info")
        let lockFile = Path.Combine(infoDir, Path.GetFileName(sourceFile) + ".info.lock")
        printfn "writing info file %s..." infoFile 
        if not (Directory.Exists infoDir) then
           Directory.CreateDirectory infoDir |> ignore
        try 
            File.WriteAllLines(infoFile, lines)
        finally
            try if Directory.Exists infoDir && File.Exists lockFile then File.Delete lockFile with _ -> ()

    let clearInfoFile sourceFile = 
        emitInfoFile sourceFile [| |]

    let LISTLIM = 20

    let (|ConsNil|_|) (v: obj) =
        let ty = v.GetType()
        if Reflection.FSharpType.IsUnion(ty) then
            let uc, vs = Reflection.FSharpValue.GetUnionFields(v, ty)
            if uc.DeclaringType.IsGenericType && uc.DeclaringType.GetGenericTypeDefinition() = typedefof<list<int>> then
                 match vs with 
                 | [| a; b |] -> Some (Some(a,b))
                 | [|  |] -> Some (None)
                 | _ -> None
            else None
        else None

    let rec (|List|_|) n (v: obj) =
        if n > LISTLIM then Some []
        else
        match v with 
        | ConsNil (Some (a,List ((+) 1 n) b)) -> Some (a::b)
        | ConsNil None -> Some []
        | _ -> None

    /// Format values resulting from live checking using the interpreter
    let rec formatValue (value: obj) = 
        match value with 
        | null -> "null/None"
        | :? string as s -> sprintf "%A" s
        | value -> 
        let ty = value.GetType()
        match value with 
        | _ when ty.Name = "Tensor" || ty.Name = "Shape" ->
            // TODO: this is a hack for DiffSharp, consider how to generalize it
            value.ToString()
        | _ when Reflection.FSharpType.IsTuple(ty) ->
            let vs = Reflection.FSharpValue.GetTupleFields(value)
            "(" + String.concat "," (Array.map formatValue vs) + ")"
        | _ when Reflection.FSharpType.IsFunction(ty)  ->
            "<func>"
        | _ when ty.IsArray ->
            let value = (value :?> Array)
            if ty.GetArrayRank() = 1 then 
                "[| " + 
                    String.concat "; " 
                       [ for i in 0 .. min LISTLIM (value.GetLength(0) - 1) -> 
                            formatValue (value.GetValue(i)) ] 
                 + (if value.GetLength(0) > LISTLIM then "; ..." else "")
                 + " |]"
            elif ty.GetArrayRank() = 2 then 
                "[| " + 
                    String.concat ";   \n " 
                       [ for i in 0 .. min (LISTLIM/2) (value.GetLength(0) - 1) -> 
                            String.concat ";" 
                               [ for j in 0 .. min (LISTLIM/2) (value.GetLength(1) - 1) -> 
                                   formatValue (value.GetValue(i, j)) ] 
                            + (if value.GetLength(1) > (LISTLIM/2) then "; ..." else "")
                       ]
                  + (if value.GetLength(0) > (LISTLIM/2) then "\n   ...\n" else "\n")
                  + " |]"
            else
                sprintf "array rank %d" value.Rank 
        | _ when Reflection.FSharpType.IsRecord(ty) ->
            let fs = Reflection.FSharpType.GetRecordFields(ty)
            let vs = Reflection.FSharpValue.GetRecordFields(value)
            "{ " + String.concat "; " [| for (f,v) in Array.zip fs vs -> f.Name + "=" + formatValue v |] + " }"
        | List 0 els ->
            "[" + String.concat "; " [| for v in els -> formatValue v |] + (if els.Length >= LISTLIM then "; .." else "") + "]"
        | _ when Reflection.FSharpType.IsUnion(ty) ->
            let uc, vs = Reflection.FSharpValue.GetUnionFields(value, ty)
            uc.Name + "(" + String.concat ", " [| for v in vs -> formatValue v |] + ")"
        | _ when (value :? System.Collections.IEnumerable) ->
            "<seq>"
        | _ ->
            value.ToString() //"unknown value"

    let MAXTOOLTIP = 100
    /// Write an info file containing extra information to make available to F# tooling.
    /// This is currently experimental and only experimental additions to F# tooling
    /// watch and consume this information.
    let writeInfoFile (tooltips: (DRange * (string * obj) list * bool)[]) sourceFile errors = 

        let lines = 
            let ranges =  HashSet<DRange>(HashIdentity.Structural)
            let havePreferred = tooltips |> Array.choose (fun (m,_,prefer) -> if prefer then Some m else None) |> Set.ofArray
            [| for (range, lines, prefer) in tooltips do
                    

                    // Only emit one line for each range. If live checks are performed twice only
                    // the first is currently shown.  
                    //
                    // We have a hack here to prefer some entries over others.  FCS returns non-compiler-generated
                    // locals for curried functions like 
                    //     a |> ... |> foo1 
                    // or
                    //     a |> ... |> foo2 x
                    //
                    // which become 
                    //     a |> ... |> (fun input -> foo input)
                    //     a |> ... |> (fun input -> foo2 x input
                    // but here a use is reported for "input" over the range of the application expression "foo1" or "foo2 x"
                    // So we prefer the actual call over these for these ranges.
                    //
                    // TODO: report this FCS problem and fix it.
                    if not (ranges.Contains(range))  && (prefer || not (havePreferred.Contains range)) then 
                        ranges.Add(range) |> ignore

                        // Format multiple lines of text into a single line in the output file
                        let valuesText = 
                            [ for (action, value) in lines do 
                                  let action = (if action = "" then "" else action + " ")
                                  let valueText = formatValue value
                                  let valueText = valueText.Replace("\n", " ").Replace("\r", " ").Replace("\t", " ")
                                  let valueText = 
                                      if valueText.Length > MAXTOOLTIP then 
                                          valueText.[0 .. MAXTOOLTIP-1] + "..."
                                      else   
                                          valueText
                                  yield action + valueText ]
                            |> String.concat "~   " // special new-line character known by experimental VS tooling + indent
                    
                        let sep = (if lines.Length = 1 then " " else "~   ")
                        let line = sprintf "ToolTip\t%d\t%d\t%d\t%d\tLiveCheck:%s%s" range.StartLine range.StartColumn range.EndLine range.EndColumn sep valuesText
                        yield line

               for (exn:exn, rangeStack) in errors do 
                    if List.length rangeStack > 0 then 
                        let range = List.last rangeStack 
                        let message = "LiveCheck failed: " + exn.Message.Replace("\t"," ").Replace("\r","   ").Replace("\n","   ") 
                        printfn "%s" message
                        let line = sprintf "Error\t%d\t%d\t%d\t%d\terror\t%s\t304" range.StartLine range.StartColumn range.EndLine range.EndColumn message
                        yield line |]

        emitInfoFile sourceFile lines

    let mutable assemblyNameId = 0
    /// Evaluate the declarations using the interpreter
    let evaluateDecls fileContents = 
        let assemblyTable = 
            dict [| for r in options.OtherOptions do 
                        //printfn "typeof<obj>.Assembly.Location = %s" typeof<obj>.Assembly.Location
                        if r.StartsWith("-r:") && not (r.Contains(".NETFramework")) && not (r.Contains("Microsoft.NETCore.App")) then 
                            let assemName = r.[3..]
                            //printfn "Script: pre-loading referenced assembly %s " assemName
                            match System.Reflection.Assembly.LoadFrom(assemName) with 
                            | null -> 
                                printfn "Script: failed to pre-load referenced assembly %s " assemName
                            | asm -> 
                                let name = asm.GetName()
                                yield (name.Name, asm) |]

        let assemblyResolver (nm: Reflection.AssemblyName) =  
            match assemblyTable.TryGetValue(nm.Name) with
            | true, res -> res
            | _ -> Reflection.Assembly.Load(nm)
                                        
        let tooltips = ResizeArray()
        let sink =
            if writeinfo then 
                { new Sink with 
                     member __.CallAndReturn(mref, callerRange, mdef, _typeArgs, args, res) = 
                         let paramNames = 
                            match mdef with 
                             | Choice1Of2 minfo -> [| for p in minfo.GetParameters() -> p.Name |]
                             | Choice2Of2 mdef -> [| for p in mdef.Parameters -> p.Name |]
                         let isValue = 
                            match mdef with 
                             | Choice1Of2 minfo -> false
                             | Choice2Of2 mdef -> mdef.IsValue
                         let lines = 
                            [ for (p, arg) in Seq.zip paramNames args do 
                                  yield (sprintf "%s:" p, arg)
                              if isValue then 
                                  yield ("value:", res.Value)
                              else
                                  yield ("return:", res.Value) ]
                         match mdef with 
                         | Choice1Of2 _ -> ()
                         | Choice2Of2 mdef -> 
                             mdef.Range |> Option.iter (fun r -> 
                                 tooltips.Add(r, lines, true))
                         match mref with 
                         | None -> ()
                         | Some mref-> 
                             callerRange |> Option.iter (fun r -> 
                                 tooltips.Add(r, lines, true))

                     member __.BindValue(vdef, value) = 
                         if not vdef.IsCompilerGenerated then 
                             vdef.Range |> Option.iter (fun r -> tooltips.Add ((r, [("", value.Value)], false)))

                     member __.BindLocal(vdef, value) = 
                         if not vdef.IsCompilerGenerated then 
                             vdef.Range |> Option.iter (fun r -> tooltips.Add ((r, [("", value.Value)], false)))

                     member __.UseLocal(vref, value) = 
                         vref.Range |> Option.iter (fun r -> tooltips.Add ((r, [("", value.Value)], false)))
                }
                |> Some
            else  
                None

        assemblyNameId <- assemblyNameId + 1
        let assemblyName = AssemblyName("Eval" + string assemblyNameId)
        let ctxt = EvalContext(assemblyName, dyntypes, assemblyResolver, ?sink=sink)
        let fileConvContents = [| for i in fileContents -> convFile i |]

        for (_, contents) in fileConvContents do 
            ctxt.AddDecls(contents.Code)

        for (sourceFile, ds) in fileConvContents do 
            printfn "evaluating decls.... " 
            let errors = ctxt.TryEvalDecls (envEmpty, ds.Code, evalLiveChecksOnly=livecheck)

            if writeinfo then 
                writeInfoFile (tooltips.ToArray()) sourceFile errors
            for (exn, locs) in errors do
                if watch then
                    printfn "fslive: exception: %A" (exn.ToString())
                    for loc in locs do 
                        printfn "   --> %O" loc
                else
                    raise exn

            printfn "...evaluated decls" 

    let mutable counter = 0
    //let produceDynamicAssembly parseTrees =
    //    //let allFlags = Array.append options.OtherOptions options.SourceFiles
    //    //for f in allFlags do
    //    //   printfn "  option %s" f
    //    counter <- counter + 1
    //    let assemName = Path.GetFileNameWithoutExtension(options.ProjectFileName) + string counter
    //    let diagnostics, _, assembly = checker.CompileToDynamicAssembly(parseTrees, assemName, [], None) |> Async.RunSynchronously  
    //    if diagnostics |> Array.exists (fun d -> d.Severity = FSharpErrorSeverity.Error) then
    //        for d in diagnostics do
    //            printfn "Compilation error: %A" d
    //        None
    //    else
    //        assembly

    let changed why _ =
        try 
            printfn "fslive: CHANGE DETECTED (%s), COMPILING...." why

            match checkFiles options.SourceFiles with 
            | Result.Error res -> Result.Error res

            | Result.Ok allFileContents -> 

            let parseTrees = List.choose fst allFileContents
            let implFiles = List.map snd allFileContents
            match webhook with 
            | Some hook ->
                sendToWebHook hook implFiles
                Result.Ok()
            | None -> 

            if not dump && webhook.IsNone then 
                //let dynAssembly =
                //    if dyntypes then 
                //        produceDynamicAssembly parseTrees
                //    else 
                //        None

                printfn "fslive: CHANGE DETECTED, RE-EVALUATING ALL INPUTS...." 
                evaluateDecls implFiles 

            // The default is to dump
            if dump && webhook.IsNone then 
                let fileConvContents = jsonFiles (Array.ofList implFiles)

                printfn "%A" fileConvContents
            Result.Ok()

        with err when watch -> 
            printfn "fslive: exception: %A" (err.ToString())
            for loc in err.EvalLocationStack do 
                printfn "   --> %O" loc
            Result.Error (None, Some err, None, None)

    for o in options.OtherOptions do 
        printfn "compiling, option %s" o

    if watch then 
        // Send an immediate changed() event
        if webhook.IsNone then 
            printfn "Sending initial changes... " 
            for sourceFile in options.SourceFiles do
                changed "initial" () |> ignore

        let mkWatcher (path, fileName) = 
            let watcher = new FileSystemWatcher(path, fileName)
            watcher.NotifyFilter <- NotifyFilters.Attributes ||| NotifyFilters.CreationTime ||| NotifyFilters.FileName ||| NotifyFilters.LastAccess ||| NotifyFilters.LastWrite ||| NotifyFilters.Size ||| NotifyFilters.Security;
            watcher.Changed.Add (changed "Changed" >> ignore)
            watcher.Created.Add (changed "Created" >> ignore)
            watcher.Deleted.Add (changed "Deleted" >> ignore)
            watcher.Renamed.Add (changed "Renamed" >> ignore)
            watcher

        let watchers = 
            [ for sourceFile in options.SourceFiles do
                let path = Path.GetDirectoryName(sourceFile)
                let fileName = Path.GetFileName(sourceFile)
                printfn "fslive: WATCHING %s in %s" fileName path 
                yield mkWatcher (path, fileName)
                if useEditFiles then 
                    let infoDir, editFile = editDirAndFile fileName
                    printfn "fslive: WATCHING %s in %s" editFile infoDir 
                    yield mkWatcher (infoDir, Path.GetFileName editFile) ]

        for watcher in watchers do
            watcher.EnableRaisingEvents <- true

        printfn "Waiting for changes... press any key to exit" 
        System.Console.ReadLine() |> ignore
        for watcher in watchers do
            watcher.EnableRaisingEvents <- false

        0
    else
        match changed "once" () with 
        | Error _ -> 1
        | Ok _ -> 0

