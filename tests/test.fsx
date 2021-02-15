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

        [| (2, 4842, [| (__SOURCE_FILE__, 36,6,36,8) |], "this is a message") |]

[<LiveCheck(10,20)>]
let f (a: int, b: int) = a + b
// tttttttttttttttttttttttt


