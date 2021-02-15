open System


[<AttributeUsage(validOn=AttributeTargets.All, AllowMultiple=true, Inherited=true)>]
type LiveCheckAttribute internal (given: obj[]) =
    inherit System.Attribute()
    new () =
        LiveCheckAttribute([| |] : obj[])
    new (argShape1: obj) =
        LiveCheckAttribute([| argShape1 |])
    new (argShape1: obj, argShape2: obj) =
        LiveCheckAttribute([| argShape1; argShape2 |])
    new (argShape1: obj, argShape2: obj, argShape3: obj) =
        LiveCheckAttribute([| argShape1; argShape2; argShape3 |])
    new (argShape1: obj, argShape2: obj, argShape3: obj, argShape4: obj) =
        LiveCheckAttribute([| argShape1; argShape2; argShape3; argShape4 |])
    new (argShape1: obj, argShape2: obj, argShape3: obj, argShape4: obj, argShape5: obj) =
        LiveCheckAttribute([| argShape1; argShape2; argShape3; argShape4; argShape5 |])
    new (argShape1: obj, argShape2: obj, argShape3: obj, argShape4: obj, argShape5: obj, argShape6: obj) =
        LiveCheckAttribute([| argShape1; argShape2; argShape3; argShape4; argShape5; argShape6 |])
    new (argShape1: obj, argShape2: obj, argShape3: obj, argShape4: obj, argShape5: obj, argShape6: obj, argShape7: obj) =
        LiveCheckAttribute([| argShape1; argShape2; argShape3; argShape4; argShape5; argShape6; argShape7 |])
    
    /// Invoked by the reflective host tooling with the right information and must give the diagnostics back
    member attr.Invoke(targetType: System.Type, 
             methLocs: (string * string * int * int * int * int)[],
             locFile: string, locStartLine: int, locStartColumn: int, locEndLine: int, locEndColumn: int) 
            : (int (* severity *) * 
               int (* number *) * 
               (string * int * int * int * int)[] *  (* location stack *)
               string (* message*))[] =

        let ctors = targetType.GetConstructors()
        let ctor = 
            ctors 
            |> Array.tryFind (fun ctor -> ctor.GetParameters().Length = given.Length)
            |> function 
               | None -> 
                   //printf "couldn't find a model constructor taking Int or Shape parameter, assuming first constructor is target of live check"
                   ctors.[0]
               | Some c -> c

        [| for meth in ctor.DeclaringType.GetMethods() do
            if not meth.ContainsGenericParameters && not meth.DeclaringType.ContainsGenericParameters then
                for attr in meth.GetCustomAttributes(typeof<LiveCheckAttribute>, true) do
                    printfn "meth %s has attr"  meth.Name
              
                    yield (2, 4842, [| (locFile, locStartLine, locStartColumn, locEndLine, locEndColumn) |], $"this is a message from checker invoke for {targetType.Name}::{meth.Name}") |]

[<LiveCheck(10,20)>]
type C(a: int, b: int) = 
    [<LiveCheck(30,40)>]
    member _.entry1(c: int, d: int) = ()
    [<LiveCheck>]
    member _.entry2(c: int, d: int) = ()

[<LiveCheck>]
let f2 : int = failwith "this is a message for failing livecheck of value"
// tttttttttttttttttttttttt


