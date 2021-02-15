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
open System.Runtime.Serialization.Formatters.Binary

[<assembly: AnalyzerAssemblyAttribute>]
do()

[<AnalyzerAttribute>]
type LiveCheckAnalyzer(ctxt) = 
    inherit FSharpAnalyzer(ctxt)

    let mutable savedTooltips = [| |]
    static member OnCheckFileImpl(checkFileResults: FSharpCheckFileResults) =
        
        let implFiles = checkFileResults.PartialAssemblyContents.ImplementationFiles 
        let dfiles = [| for i in implFiles -> i.FileName, { Code = Convert(includeRanges=true, tolerateIncomplete=true).ConvertDecls i.Declarations } |]
        let request : LiveCheckRequest = { Files = dfiles; OtherOptions = [| for r in checkFileResults.ProjectContext.GetReferencedAssemblies() do if r.FileName.IsSome then "-r:" + r.FileName.Value |] }
        let infile = Path.GetTempFileName()
        let outfile = Path.GetTempFileName()
        begin 
            let formatter = new BinaryFormatter()
            use out = File.OpenWrite(infile)
            formatter.Serialize(out, request)
        end
        let path = Path.Combine(Path.GetDirectoryName(typeof<LiveCheckAnalyzer>.Assembly.Location), "../../fslive/fslive.dll")
        let pw6432 = Environment.GetEnvironmentVariable("ProgramW6432")
        let p = new Process()
        p.StartInfo.FileName <- pw6432 + @"\dotnet\dotnet.exe"
        p.StartInfo.WorkingDirectory <- Path.GetDirectoryName(checkFileResults.ProjectContext.ProjectOptions.ProjectFileName)
        p.StartInfo.Arguments <- $"\"{path}\" --inbin {infile} --outbin {outfile}"
        printfn $"starting {p.StartInfo.FileName} {p.StartInfo.Arguments}"
        p.Start() |> ignore
        p.WaitForExit()
        let resp =
            let formatter = new BinaryFormatter()
            use s = File.OpenRead(outfile)
            formatter.Deserialize(s) :?> LiveCheckResponse

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
