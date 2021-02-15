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
    do printfn "makign analyzer"

    let mutable savedTooltips = [| |]
    static let p =
      lazy
        let path = Path.Combine(Path.GetDirectoryName(typeof<LiveCheckAnalyzer>.Assembly.Location), "../../fslive/netcoreapp3.1/fslive.dll")
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
        printfn $"starting \"{psi.FileName}\" {psi.Arguments}"
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

        let toolTipsToSave = (Array.tryLast resp.FileResults) |> function None -> [| |] | Some r -> r.Tooltips
        let diags = 
            [| for d in ((Array.tryLast resp.FileResults) |> function None -> [| |] | Some r -> r.Diagnostics) do 
                let sev = match d.Severity with 2 -> FSharpDiagnosticSeverity.Error | _ -> FSharpDiagnosticSeverity.Warning
                let m = Range.mkRange d.Location.File (Position.mkPos d.Location.StartLine d.Location.StartColumn) (Position.mkPos d.Location.EndLine d.Location.EndColumn)
                yield FSharpDiagnostic.Create(sev, d.Message, d.Number, m) |]
        diags, toolTipsToSave
        
    static member TryAdditionalToolTipImpl(savedTooltips: (DRange * string list)[], fileName, pos: Position) =
        [| for (m, lines) in savedTooltips do
            if m.File = fileName && 
                m.StartLine <= pos.Line && pos.Line <= m.EndLine &&
                (pos.Line <> m.StartLine || m.StartColumn <= pos.Column) && 
                (pos.Line <> m.EndLine|| pos.Column <= m.EndColumn) then
                for line in lines do
                    yield TaggedText.tagText line |]

    override _.OnCheckFile(fileCtxt) =
       
        let diags, toolTipsToSave = LiveCheckAnalyzer.OnCheckFileImpl(fileCtxt.CheckerModel)
        savedTooltips <- toolTipsToSave
        diags
        
    override _.TryAdditionalToolTip(fileCtxt, pos) =
        let fileName = fileCtxt.CheckerModel.ParseTree.Value.FileName
        let lines = LiveCheckAnalyzer.TryAdditionalToolTipImpl(savedTooltips, fileName, pos)
        if lines.Length = 0 then None else Some lines

    override _.RequiresAssemblyContents = true
