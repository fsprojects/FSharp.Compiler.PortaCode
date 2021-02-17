namespace FsLive.Tools.LiveCheckAnalyzer

open FSharp.Core.CompilerServices
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Diagnostics
open FSharp.Compiler.Text
open FSharp.Compiler.Symbols
open FSharp.Compiler.PortaCode.CodeModel
open FSharp.Compiler.PortaCode.FromCompilerService
open System
open System.Diagnostics
open System.IO
open System.Runtime.Serialization
open System.Runtime.Serialization.Formatters
open System.Runtime.Serialization.Formatters.Binary

[<assembly: AnalyzerAssemblyAttribute>]
do()

type TempFile() =
    let file = Path.GetTempFileName()
    interface IDisposable with member x.Dispose() = File.Delete(file)
    member x.Path = file

[<AnalyzerAttribute>]
type LiveCheckAnalyzer(ctxt) = 
    inherit FSharpAnalyzer(ctxt)
    do printfn "FsLive.Tools.LiveCheckAnalyzer: creating LiveCheckAnalyzer"

    let mutable savedTooltips = [| |]
    static let toRange (m: DRange) =
        Range.mkRange m.File (Position.mkPos m.StartLine m.StartColumn) (Position.mkPos m.EndLine m.EndColumn)

    static let p =
      lazy
        let path = Path.Combine(Path.GetDirectoryName(typeof<LiveCheckAnalyzer>.Assembly.Location), "../../fslive/net5.0/fslive.dll")
        let pw6432 = Environment.GetEnvironmentVariable("ProgramW6432")
        let psi = 
            ProcessStartInfo(
                FileName = pw6432 + @"\dotnet\dotnet.exe",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                WorkingDirectory = Path.GetTempPath(),
                Arguments = $"\"{path}\" --daemon")
        printfn $"FsLive.Tools.LiveCheckAnalyzer: starting \"{psi.FileName}\" {psi.Arguments}"
        let p = Process.Start(psi)
        System.AppDomain.CurrentDomain.ProcessExit.Add(fun _ -> try p.Kill() with _ -> ())
        p

    static member OnCheckFileImpl(checkFileResults: FSharpCheckFileResults) =
        
        let implFiles = checkFileResults.PartialAssemblyContents.ImplementationFiles 
        let dfiles = [| for i in implFiles -> i.FileName, { Code = Convert(includeRanges=true, tolerateIncomplete=true).ConvertDecls i.Declarations } |]
        let request : LiveCheckRequest = { Files = dfiles; OtherOptions = [| for r in checkFileResults.ProjectContext.GetReferencedAssemblies() do if r.FileName.IsSome then "-r:" + r.FileName.Value |] }
        use infile = new TempFile()
        use outfile = new TempFile()
        begin 
            let formatter = new BinaryFormatter()
            use out = File.OpenWrite(infile.Path)
            formatter.Serialize(out, request)
        end
        p.Value.StandardInput.WriteLine($"REQUEST %s{infile.Path} %s{outfile.Path}")
        let expected = $"RESPONSE %s{infile.Path} %s{outfile.Path}"
        let mutable line = ""
        while (line <- p.Value.StandardOutput.ReadLine(); 
               if line = expected then 
                   false
               else
                   printfn "%s" line
                   true) do ()
        let resp =
            let formatter = new BinaryFormatter()
            let handler = ResolveEventHandler(fun sender args -> if args.Name = typeof<DFile>.Assembly.GetName().Name then typeof<DFile>.Assembly else null)
            try 
                System.AppDomain.CurrentDomain.add_AssemblyResolve handler
            
                use s = File.OpenRead(outfile.Path)
                formatter.Deserialize(s) :?> LiveCheckResponse
            finally 
                System.AppDomain.CurrentDomain.remove_AssemblyResolve handler

        let toolTipsToSave =
            resp.FileResults
            |> Array.tryLast 
            |> function 
                | None -> [| |]
                | Some r -> r.Tooltips

        let diags = 
            [| for d in ((Array.tryLast resp.FileResults) |> function None -> [| |] | Some r -> r.Diagnostics) do 
                let sev = match d.Severity with 2 -> FSharpDiagnosticSeverity.Error | _ -> FSharpDiagnosticSeverity.Warning
                let m = toRange d.Location
                yield FSharpDiagnostic.Create(sev, d.Message, d.Number, m) |]

        diags, toolTipsToSave
        
    static member TryAdditionalToolTipImpl(savedTooltips: LiveCheckResultTooltip[], fileName, pos: Position) =
        [| for savedTooltip in savedTooltips do
            let m = savedTooltip.Location
            if m.File = fileName && 
                m.StartLine <= pos.Line && pos.Line <= m.EndLine &&
                (pos.Line <> m.StartLine || m.StartColumn <= pos.Column) && 
                (pos.Line <> m.EndLine|| pos.Column <= m.EndColumn) then
                for line in savedTooltip.Lines do
                    for part in line do
                       let tt = 
                           match part.Text with
                           | (* "text" *) _ -> TaggedText.tagText part.Text
                       match part.NavigateToFile with
                       | Some m2 -> NavigableTaggedText(tt, toRange m2) :> TaggedText
                       | _ -> 
                       match part.NavigateToLink with
                       | Some url -> WebLinkTaggedText(tt, System.Uri(url)) :> TaggedText
                       | _ -> 
                       tt
                       |]

    override _.OnCheckFile(fileCtxt) =
        let diags, toolTipsToSave = LiveCheckAnalyzer.OnCheckFileImpl(fileCtxt.CheckerModel)
        savedTooltips <- toolTipsToSave
        diags
        
    override _.TryAdditionalToolTip(fileCtxt, pos) =
        let fileName = fileCtxt.CheckerModel.ParseTree.Value.FileName
        let lines = LiveCheckAnalyzer.TryAdditionalToolTipImpl(savedTooltips, fileName, pos)
        if lines.Length = 0 then None else Some lines

    override _.RequiresAssemblyContents = true
