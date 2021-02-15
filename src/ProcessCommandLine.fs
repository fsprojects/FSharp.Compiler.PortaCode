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
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Symbols
open FSharp.Compiler.Text
open System.Net
open System.Text
open System.Runtime.Serialization.Formatters.Binary

let checker = FSharpChecker.Create(keepAssemblyContents = true)

let ProcessCommandLine (argv: string[]) =
    let mutable fsproj = None
    let mutable inbin = None
    let mutable outbin = None
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
                elif arg.StartsWith "--projarg:" then msbuildArgs <- msbuildArgs @ [ arg.["--projarg:".Length ..]] 
                elif arg.StartsWith "--define:" then otherFlags <- otherFlags @ [ arg ]
                elif arg.StartsWith "--inbin:" then inbin <- Some arg.["--inbin:".Length ..]
                elif arg.StartsWith "--outbin:" then inbin <- Some arg.["--outbin:".Length ..]
                elif arg = "--once" then watch <- false
                elif arg = "--dump" then dump <- true
                elif arg = "--livecheck" then 
                    dyntypes <- true
                    livecheck <- true
                    writeinfo <- true
                    //useEditFiles <- true
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
                   printfn "   --livecheck       Only evaluate those with a *CheckAttribute (e.g. LiveCheck or ShapeCheck)"
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
                for e in parseResults.Diagnostics do   
                   printfn "Error: %A" e
                failwith "unexpected aborted"
                Result.Error (parseResults.ParseTree, None, None, None)

            | FSharpCheckFileAnswer.Succeeded res -> 
                let mutable hasErrors = false
                for error in res.Diagnostics do 
                    printfn "%s" (error.ToString())
                    if error.Severity = FSharpDiagnosticSeverity.Error then 
                        hasErrors <- true

                if hasErrors then 
                    Result.Error (parseResults.ParseTree, None, Some res.Diagnostics, res.ImplementationFile)
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
    let tolerateIncompleteExpressions = livecheck && watch
    let convFile (i: FSharpImplementationFileContents) =         
        //(i.QualifiedName, i.FileName
        i.FileName, { Code = Convert(keepRanges, tolerateIncompleteExpressions).ConvertDecls i.Declarations }

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

    let emitInfoFile (sourceFile: string) lines = 
        let infoDir = Path.Combine(Path.GetDirectoryName(sourceFile), ".fsharp")
        let infoFile = Path.Combine(infoDir, Path.GetFileName(sourceFile) + ".info")
        let lockFile = Path.Combine(infoDir, Path.GetFileName(sourceFile) + ".info.lock")
        printfn "writing info file %s..." infoFile 
        if not (Directory.Exists infoDir) then
           Directory.CreateDirectory infoDir |> ignore
        try 
            File.WriteAllLines(infoFile, lines, encoding=Encoding.Unicode)
        finally
            try if Directory.Exists infoDir && File.Exists lockFile then File.Delete lockFile with _ -> ()

    /// Write an info file containing extra information to make available to F# tooling.
    /// This is currently experimental and only experimental additions to F# tooling
    /// watch and consume this information.
    let writeInfoFile (formattedTooltips: (DRange * string list)[]) sourceFile (diags: DDiagnostic[]) = 

        let lines = 
            [| for (range, valuesLines) in formattedTooltips do
                    
                    let sep = (if valuesLines.Length = 1 then " " else "\\n")
                    let valuesText = valuesLines |> String.concat "\\n  " // special new-line character known by experimental VS tooling + indent

                    let line = sprintf "ToolTip\t%d\t%d\t%d\t%d\tLiveCheck:%s%s" range.StartLine range.StartColumn range.EndLine range.EndColumn sep valuesText
                    yield line 

               for diag in diags do 
                    printfn "%s" (diag.ToString())
                    for range in diag.LocationStack do
                        if Path.GetFullPath(range.File) = Path.GetFullPath(sourceFile) then
                            let message = 
                               "LiveCheck: " + diag.Message + 
                               ([| for m in Array.rev diag.LocationStack -> sprintf "\n  stack: (%d,%d)-(%d,%d) %s" m.StartLine m.StartColumn m.EndLine m.EndColumn m.File |] |> String.concat "")
                            let message = message.Replace("\t"," ").Replace("\r","").Replace("\n","\\n") 
                            let sev = match diag.Severity with 0 | 1 -> "warning" | _ -> "error"
                            let line = sprintf "Error\t%d\t%d\t%d\t%d\t%s\t%s\t%d" range.StartLine range.StartColumn range.EndLine range.EndColumn sev message diag.Number
                            yield line |]

        lines

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

    let mutable lastCompileStart = System.DateTime.Now
    let changed why _ =
        try 
            printfn "fslive: COMPILING (%s)...." why
            lastCompileStart <- System.DateTime.Now

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
                printfn "fslive: EVALUATING ALL INPUTS...." 
                let fileConvContents = 
                    [| for i in implFiles -> 
                         let code = { Code = Convert(keepRanges, tolerateIncompleteExpressions).ConvertDecls i.Declarations }
                         i.FileName, code |]

                let evaluator = LiveCheckEvaluation(options.OtherOptions, dyntypes, writeinfo, livecheck)
                let results = evaluator.EvaluateDecls fileConvContents 
                let mutable res = Ok()
                for (sourceFile, diags, formattedTooltips) in results do   
                    let info = 
                        if writeinfo then 
                            writeInfoFile formattedTooltips sourceFile diags
                        else
                            [| |]
                    for diag in diags do
                        printfn "%s" (diag.ToString())
                    if diags |> Array.exists (fun diag -> diag.Severity >= 2) then res <- Error ()

                    printfn "...evaluated decls" 
                    emitInfoFile sourceFile info
                match res with
                | Error _ when not watch -> exit 1
                | _ -> ()

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

    match inbin, outbin with
    | Some inf, Some outf ->
        let req: LiveCheckRequest = 
            let formatter = new BinaryFormatter()
            use s = File.OpenRead(inf)
            formatter.Deserialize(s) :?> _
        let evaluator = LiveCheckEvaluation(req.OtherOptions, dyntypes=true, writeinfo=true, livecheck=true)
        let results = evaluator.EvaluateDecls req.Files
        let resp = { FileResults = [| for (f,diags,tt) in results -> {File=f;Diagnostics=diags;Tooltips=tt} |]}
        begin 
            let formatter = new BinaryFormatter()
            use out = File.OpenWrite(outf)
            formatter.Serialize(out, (resp: LiveCheckResponse))
        end
        1
    | _ -> 
    if watch then 
        // Send an immediate changed() event
        if webhook.IsNone then 
            printfn "Sending initial changes... " 
            changed "initial" () |> ignore

        let mkWatcher (sourceFile: string) = 
            let path = Path.GetDirectoryName(sourceFile)
            let fileName = Path.GetFileName(sourceFile)
            printfn "fslive: WATCHING %s in %s" fileName path 
            let watcher = new FileSystemWatcher(path, fileName)
            watcher.NotifyFilter <- NotifyFilters.Attributes ||| NotifyFilters.CreationTime ||| NotifyFilters.FileName ||| NotifyFilters.LastAccess ||| NotifyFilters.LastWrite ||| NotifyFilters.Size ||| NotifyFilters.Security;

            let fileChange msg e = 
                let lastWriteTime = try max (File.GetCreationTime(sourceFile)) (File.GetLastWriteTime(sourceFile)) with _ -> DateTime.MaxValue
                printfn "change %s, lastCOmpileStart=%A, lastWriteTime = %O"  sourceFile lastCompileStart lastWriteTime
                if lastWriteTime > lastCompileStart then
                    printfn "changed %s"  sourceFile 
                    changed msg e |> ignore

            watcher.Changed.Add (fileChange (sprintf "Changed %s" fileName))
            watcher.Created.Add (fileChange (sprintf "Created %s" fileName))
            watcher.Deleted.Add (fileChange (sprintf "Deleted %s" fileName))
            watcher.Renamed.Add (fileChange (sprintf "Renamed %s" fileName))
            watcher

        let watchers = 
            [ for sourceFile in options.SourceFiles do
                yield mkWatcher sourceFile
                if useEditFiles then 
                    yield mkWatcher sourceFile ]

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

