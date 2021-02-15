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
        Assert.AreEqual(diags.Length, 3)
        let expected = 
            [ "this is a message from checker invoke for C::entry1"
              "this is a message from checker invoke for C::entry2"
              "this is a message for failing livecheck of value" ]
        let actual = 
            [ for d in diags -> d.Message ]
         
        if actual <> expected then 
            failwith $"%A{actual} <> %A{expected}"
    | _ -> failwith "nope"
