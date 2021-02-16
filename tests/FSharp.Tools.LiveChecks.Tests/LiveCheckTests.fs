module Tests.LiveCheckAnalyzerTests

open System
open System.IO
open NUnit.Framework
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Text
open FsUnit

[<Test>]
let AnalyzerSmokeTest () =
    let checker = FSharpChecker.Create(keepAssemblyContents=true)
    let sourceFile = __SOURCE_DIRECTORY__ + "/test.fsx"
    let text = SourceText.ofString (File.ReadAllText(sourceFile))
    let opts, optsDiags = checker.GetProjectOptionsFromScript(sourceFile,text) |> Async.RunSynchronously
    Assert.AreEqual(optsDiags.Length, 0)
    let _parseResults, checkAnswer = checker.ParseAndCheckFileInProject(sourceFile,0,text,opts) |> Async.RunSynchronously
    let _analyzer = FsLive.Tools.LiveCheckAnalyzer.LiveCheckAnalyzer(Unchecked.defaultof<FSharpAnalysisContext>)
    match checkAnswer with 
    | FSharpCheckFileAnswer.Succeeded checkResults -> 
        let diags, toolTips = FsLive.Tools.LiveCheckAnalyzer.LiveCheckAnalyzer.OnCheckFileImpl(checkResults)
        printfn $"%A{diags}"

        let expectedExtraDiagnostics = 
            ["""this is a message from checker invoke for ctor on type C with sub-methods [|"entry1"; "entry2"|]"""
             """this is a message from checker invoke for static method entry3""";
             """this is a message for failing livecheck of value""";
             """this is a message from checker invoke for static method f2""" ]
        
        // Check that the LiveCheck execution collects diagnostics
        let actualExtraDiagnostics = 
            [ for d in diags -> d.Message ]

        if actualExtraDiagnostics <> expectedExtraDiagnostics then 
            failwith $"%A{actualExtraDiagnostics} <> %A{expectedExtraDiagnostics}"

        let actualExtraTooltips = [ for (_m, msgs) in toolTips do yield! msgs ]

        // Check that the LiveCheck execution collects tooltips about two values
        if actualExtraTooltips |> List.contains "123456" |> not then failwithf "%A" actualExtraTooltips
        if actualExtraTooltips |> List.contains "\"this value should appear in additional tooltips\"" |> not then failwithf "%A" actualExtraTooltips
         
    | _ -> failwith "nope"
