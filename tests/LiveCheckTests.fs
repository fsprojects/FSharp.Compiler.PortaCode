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
    let parseResults, checkAnswer = checker.ParseAndCheckFileInProject(sourceFile,0,text,opts) |> Async.RunSynchronously
    let analyzer = FsLive.Tools.LiveCheckAnalyzer.LiveCheckAnalyzer(Unchecked.defaultof<FSharpAnalysisContext>)
    match checkAnswer with 
    | FSharpCheckFileAnswer.Succeeded checkResults -> 
       let diags, toolTips = FsLive.Tools.LiveCheckAnalyzer.LiveCheckAnalyzer.OnCheckFileImpl(checkResults)
       Assert.AreEqual(diags.Length, 1)
       ()
    | _ -> failwith "nope"
