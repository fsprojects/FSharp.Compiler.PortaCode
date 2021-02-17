
#compilertool @"e:\GitHub\dsyme\FSharp.Compiler.PortaCode\FSharp.Tools.LiveChecks\bin\Debug\netstandard2.0\FSharp.Tools.LiveChecks.dll"
 
//#r "aa"
open System
open System.Reflection


[<AttributeUsage(validOn=AttributeTargets.All, AllowMultiple=true, Inherited=true)>]
type LiveCheckAttribute internal (given: int[]) =
    inherit System.Attribute()
    new () =
        LiveCheckAttribute([| |] : int[])
    new (argShape1: int) =
        LiveCheckAttribute([| argShape1 |])
    new (argShape1: int, argShape2: int) =
        LiveCheckAttribute([| argShape1; argShape2 |])
    
    /// Invoked by the reflective host tooling with the right information and must give the diagnostics back
    member attr.RunChecks(target: obj,
             loc: (string * int * int * int * int), 
             methLocs: (MethodInfo * obj * (string * int * int * int * int))[]) 
            : (int (* severity *) * 
               int (* number *) * 
               (string * int * int * int * int)[] *  (* location stack *)
               string (* message*))[] =

        match target with 
        | :? System.Type as targetType -> 
            let methNames = [| for (minfo, _, _) in methLocs -> minfo.Name |]
            [| (2, 4842, [| loc |], $"this is a message from checker invoke for ctor on type {targetType.Name} with sub-methods %A{methNames}") |]
        | :? System.Reflection.MethodInfo as targetMethod -> 
            [| (2, 4843, [| loc |], $"this is a message from checker invoke for static method {targetMethod.Name}") |]
        | _ -> [| |]

[<LiveCheck(10,20)>]
type C(a: int, b: int) = 
    [<LiveCheck(30,40)>]
    member _.entry1(c: int, d: int) = ()
    [<LiveCheck>]
    member _.entry2(c: int, d: int) = ()

[<LiveCheck(10,20)>]
let entry3(c: int, d: int) = ()

[<LiveCheck>]
let f2 : int = 
    let aaaaaa = 123456 // this value should appear in additional tooltips
    let bbbbb = "this value should appear in additional tooltips"
    failwith "this is a message for failing livecheck of value"
// tttttttttttttttttttttttt


