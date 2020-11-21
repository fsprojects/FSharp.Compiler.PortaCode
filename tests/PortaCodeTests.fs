module FSharp.Compiler.PortaCode.Tests.Basic

open System
open System.IO
open NUnit.Framework
open FsUnit

[<AutoOpen>]
module TestHelpers = 

    let internal SimpleTestCase livecheck dyntypes name code = 
        let directory = __SOURCE_DIRECTORY__ + "/data"
        Directory.CreateDirectory directory |> ignore
        Environment.CurrentDirectory <- directory
        File.WriteAllText (name + ".fsx", """
module TestCode
""" + code)

        let args = 
            [| yield "--once"; 
               if livecheck then yield "--livecheck"; 
               if dyntypes then yield "--dyntypes"; 
               yield name + ".fsx" 
               |]
        let res = FSharp.Compiler.PortaCode.ProcessCommandLine.ProcessCommandLine(args)
        Assert.AreEqual(0, res)

[<TestCase(true)>]
[<TestCase(false)>]
let TestTuples (dyntypes: bool) =
    SimpleTestCase false dyntypes "TestTuples" """
module Tuples = 
    let x1 = (1, 2)
    let x2 = match x1 with (a,b) -> 1 + 2
        """

[<TestCase(true)>]
[<TestCase(false)>]
let SmokeTestLiveCheck (dyntypes: bool) =
    SimpleTestCase true dyntypes "SmokeTestLiveCheck" """
module SmokeTestLiveCheck = 
    type LiveCheckAttribute() = 
        inherit System.Attribute()
    
    let mutable v = 0
    
    let x1 = 
        v <- v + 1
        4 

    [<LiveCheck>]
    let x2 = 
        v <- v + 1
        4 

    [<LiveCheck>]
    let x3 = 
        // For live checking, bindings are executed on-demand
        // 'v' is only incremented once - because `x1` is not yet evaluated!
        if v <> 1 then failwithf "no way John, v = %d" v

        let y1 = x2 + 3
        
        // 'v' has not been incremented again - because `x2` is evaluated once!
        if v <> 1 then failwithf "no way Jane, v = %d" v
        if y1 <> 7 then failwithf "no way Juan, y1 = %d" y1

        let y2 = x1 + 1

        // 'v' has been incremented - because `x1` is now evaluated!
        if v <> 2 then failwithf "no way Julie, v = %d" v
        if y2 <> 5 then failwithf "no way Jose, y2 = %d, v = %d" y2 v

        let y3 = x1 + 1

        // 'v' is not incremented again - because `x1` is already evaluated!
        if v <> 2 then failwithf "no way Julie, v = %d" v
        if y3 <> 5 then failwithf "no way Jose, y3 = %d, v = %d" y3 v

        5 

    let x4 : int = failwith "no way"
        """

[<TestCase(true)>]
[<TestCase(false)>]
let SmokeTestShapeCheck (dyntypes: bool) =
    SimpleTestCase true dyntypes "SmokeTestShapeCheck" """
module SmokeTestShapeCheck = 
    type ShapeCheckAttribute() = 
        inherit System.Attribute()
    
    let mutable v = 0
    
    let x1 = 
        v <- v + 1
        4 

    [<ShapeCheck>]
    let x2 = 
        v <- v + 1
        4 

    [<ShapeCheck>]
    let x3 = 
        // For live checking, bindings are executed on-demand
        // 'v' is only incremented once - because `x1` is not yet evaluated!
        if v <> 1 then failwithf "no way John, v = %d" v

        let y1 = x2 + 3
        
        // 'v' has not been incremented again - because `x2` is evaluated once!
        if v <> 1 then failwithf "no way Jane, v = %d" v
        if y1 <> 7 then failwithf "no way Juan, y1 = %d" y1

        let y2 = x1 + 1

        // 'v' has been incremented - because `x1` is now evaluated!
        if v <> 2 then failwithf "no way Julie, v = %d" v
        if y2 <> 5 then failwithf "no way Jose, y2 = %d, v = %d" y2 v

        let y3 = x1 + 1

        // 'v' is not incremented again - because `x1` is already evaluated!
        if v <> 2 then failwithf "no way Julie, v = %d" v
        if y3 <> 5 then failwithf "no way Jose, y3 = %d, v = %d" y3 v

        5 

    let x4 : int = failwith "no way"
        """
[<TestCase(true)>]
[<TestCase(false)>]
let PlusOperator (dyntypes: bool) =
    SimpleTestCase false dyntypes "PlusOperator" """
module PlusOperator = 
    let x1 = 1 + 1
    let x5 = 1.0 + 2.0
    let x6 = 1.0f + 2.0f
    let x7 = 10uy + 9uy
    let x8 = 10us + 9us
    let x9 = 10u + 9u
    let x10 = 10UL + 9UL
    let x11 = 10y + 9y
    let x12 = 10s + 9s
    let x14 = 10 + 9
    let x15 = 10L + 9L
    let x16 = 10.0M + 11.0M
    let x17 = "a" + "b"
        """

[<TestCase(true)>]
[<TestCase(false, Ignore= "fails without dynamic emit types")>]
let ImplementClassOverride(dyntypes: bool) =
    SimpleTestCase false dyntypes "ImplementClassOverride" """

type UserType() =
    override x.ToString() = "a"
let f () = 
    let u = UserType()
    let s = u.ToString()
    if s <> "a" then failwithf "unexpected, got '%s', expected 'a'" s 

f()
"""


//[<TestCase(true)>]
//[<TestCase(false, Ignore= "fails without dynamic emit types")>]
//let ImplementClassOverrideInGenericClass(dyntypes: bool) =
//    SimpleTestCase false dyntypes "ImplementClassOverrideInGenericClass" """

//type UserType<'T>(x:'T) =
//    override _.ToString() : string = unbox x
//let f () = 
//    let u : UserType<string> = UserType<string>("a")
//    let s = u.ToString()
//    if s <> "a" then failwithf "unexpected, got '%s', expected 'a'" s 

//f()
//"""


[<TestCase(true)>]
[<TestCase(false)>]
let SetMapCount(dyntypes: bool) =
    SimpleTestCase false dyntypes "SetMapCount" """

let f () = 
    let l = [ 1; 2; 3 ]
    let s = Set.ofList [ 1; 2; 3 ]
    let m = Map.ofList [ (1,1) ]
    if l.Length <> 3 then failwith "unexpected"
    if s.Count <> 3 then failwith "unexpected"
    if m.Count <> 1 then failwith "unexpected"

f()
"""

[<TestCase(true)>]
[<TestCase(false, Ignore="needs dyntypes")>]
let CustomAttributeSmokeTest(dyntypes: bool) =
    SimpleTestCase false dyntypes "CustomAttributeSmokeTest" """

open System
[<Obsolete>]
type UserType() = member x.P = 1

let attrs = typeof<UserType>.GetCustomAttributes(typeof<ObsoleteAttribute>, true)
if attrs.Length <> 1 then failwith "unexpected"

"""

[<TestCase(true)>]
[<TestCase(false, Ignore="needs dyntypes")>]
let CustomAttributeWithArgs(dyntypes: bool) =
    SimpleTestCase false dyntypes "CustomAttributeWithArgs" """

open System
[<Obsolete("abc")>]
type UserType() = member x.P = 1

let attrs = typeof<UserType>.GetCustomAttributes(typeof<ObsoleteAttribute>, true)
if attrs.Length <> 1 then failwith "unexpected"
if (attrs.[0] :?> ObsoleteAttribute).Message <> "abc" then failwith "unexpected"

"""

[<TestCase(true)>]
[<TestCase(false)>]
let ArrayOfUserDefinedUnionType(dyntypes: bool) =
    SimpleTestCase false dyntypes "ArrayOfUserDefinedUnionType" """

type UserType = A of int | B
let f () = 
    let a = [| UserType.A 1 |]
    if a.Length <> 1 then failwith "unexpected"

f()
"""

[<TestCase(true)>]
[<TestCase(false)>]
let ArrayOfUserDefinedRecordType(dyntypes: bool) =
    SimpleTestCase false dyntypes "ArrayOfUserDefinedUnionRecordType" """

type UserType = { X: int; Y: string }
let f () = 
    let a = [| { X = 1; Y = "a" } |]
    if a.Length <> 1 then failwith "unexpected"

f()
"""


[<TestCase(true)>]
[<TestCase(false)>]
let ArrayOfUserDefinedAnonRecordType(dyntypes: bool) =
    SimpleTestCase false dyntypes "ArrayOfUserDefinedUnionRecordType" """

let f () = 
    let a = [| {| X = 1; Y = "a" |} |]
    if a.Length <> 1 then failwith "unexpected"
    if a.[0].X <> 1 then failwith "unexpected"
    if a.[0].Y <> "a" then failwith "unexpected"

f()
"""

[<TestCase(true)>]
[<TestCase(false)>]
let SetOfUserDefinedUnionType(dyntypes: bool) =
    SimpleTestCase false dyntypes "SetOfUserDefinedUnionType" """

type UserType = A of int | B
let f () = 
    let a = set [| UserType.A 1 |]
    if a.Count <> 1 then failwith "unexpected"

f()
"""

[<TestCase(true)>]
[<TestCase(false)>]
let MinusOperator (dyntypes: bool) =
    SimpleTestCase false dyntypes "MinusOperator" """
module MinusOperator = 
    let x1 = 1 - 1
    let x5 = 1.0 - 2.0
    let x6 = 1.0f - 2.0f
    let x7 = 10uy - 9uy
    let x8 = 10us - 9us
    let x9 = 10u - 9u
    let x10 = 10UL - 9UL
    let x11 = 10y - 9y
    let x12 = 10s - 9s
    let x14 = 10 - 9
    let x15 = 10L - 9L
    let x16 = 10.0M - 11.0M
        """

[<TestCase(true)>]
[<TestCase(false)>]
let Options (dyntypes: bool) =
    SimpleTestCase false dyntypes "Options" """
module Options = 
    let x2 = None : int option 
    let x3 = Some 3 : int option 
    let x5 = x2.IsNone
    let x6 = x3.IsNone
    let x7 = x2.IsSome
    let x8 = x3.IsSome
        """

[<TestCase(true)>]
[<TestCase(false)>]
let Exceptions (dyntypes: bool) =
    SimpleTestCase false dyntypes "Exceptions" """
module Exceptions = 
    let x2 = try invalidArg "a" "wtf" with :? System.ArgumentException -> () 
    let x4 = try failwith "hello" with e -> () 
    let x5 = try 1 with e -> failwith "fail!" 
    if x5 <> 1 then failwith "fail! fail!" 
        """

[<TestCase(true)>]
[<TestCase(false)>]
let TestEvalIsNone (dyntypes: bool) =
    SimpleTestCase false dyntypes "TestEvalIsNone" """
let x3 = (Some 3).IsNone
        """

[<TestCase(true)>]
[<TestCase(false)>]
let TestEvalUnionCaseInGenericCode (dyntypes: bool) =
    SimpleTestCase false dyntypes "TestEvalUnionCaseInGenericCofe" """
let f<'T>(x:'T) = Some x

let y = f 3
printfn "y = %A, y.GetType() = %A" y (y.GetType())
        """
[<TestCase(true)>]
[<TestCase(false)>]
let TestEvalNewOnClass(dyntypes: bool) =
    SimpleTestCase false dyntypes "TestEvalNewOnClass" """
type C(x: int) = 
    member __.X = x

let y = C(3)
let z = if y.X <> 3 then failwith "fail!" else 1
        """

[<TestCase(true)>]
[<TestCase(false)>]
let TestExtrinsicFSharpExtensionOnClass1(dyntypes: bool) =
    SimpleTestCase false dyntypes "TestExtrinsicFSharpExtensionOnClass1" """
type System.String with 
    member x.GetLength() = x.Length

let y = "a".GetLength() 
let z = if y <> 1 then failwith "fail!" else 1
        """

[<TestCase(true)>]
[<TestCase(false)>]
let TestExtrinsicFSharpExtensionOnClass2(dyntypes: bool) =
    SimpleTestCase false dyntypes "TestExtrinsicFSharpExtensionOnClass2" """
type System.String with 
    member x.GetLength2(y:int) = x.Length + y

let y = "ab".GetLength2(5) 
let z = if y <> 7 then failwith "fail!" else 1
        """

[<TestCase(true)>]
[<TestCase(false)>]
let TestExtrinsicFSharpExtensionOnClass3(dyntypes: bool) =
    SimpleTestCase false dyntypes "TestExtrinsicFSharpExtensionOnClass3" """
type System.String with 
    static member GetLength3(x:string) = x.Length

let y = System.String.GetLength3("abc") 
let z = if y <> 3 then failwith "fail!" else 1
        """

[<TestCase(true)>]
[<TestCase(false)>]
let TestExtrinsicFSharpExtensionOnClass4(dyntypes: bool) =
    SimpleTestCase false dyntypes "TestExtrinsicFSharpExtensionOnClass4" """
type System.String with 
    member x.LengthProp = x.Length

let y = "abcd".LengthProp
let z = if y <> 4 then failwith "fail!" else 1
        """

[<TestCase(true)>]
[<TestCase(false)>]
let TestTopMutables(dyntypes: bool) =
    SimpleTestCase false dyntypes "TestTopFunctionIsNotValue" """
let mutable x = 0
if x <> 0 then failwith "failure A!" else 1
let y(c:int) = 
    (x <- x + 1
     x)
let z1 = y(3)
if x <> 1 then failwith "failure B!" else 1
let z2 = y(4)
if x <> 2 then failwith "failure C!" else 1
if z1 <> 1 || z2 <> 2 then failwith "failure D!" else 1
        """

[<TestCase(true)>]
[<TestCase(false)>]
let TestTopFunctionIsNotValue(dyntypes: bool) =
    SimpleTestCase false dyntypes "TestTopFunctionIsNotValue" """
let mutable x = 0
if x <> 0 then failwith "failure A!" else 1
let y(c:int) = 
    (x <- x + 1
     x)
let z1 = y(1)
if x <> 1 then failwith "failure B!" else 1
let z2 = y(2)
if x <> 2 then failwith "failure C!" else 1
if z1 <> 1 || z2 <> 2 then failwith "failure D!" else 1
        """

[<TestCase(true)>]
[<TestCase(false)>]
let TestTopUnitValue(dyntypes: bool) =
    SimpleTestCase false dyntypes "TestTopUnitValue" """
let mutable x = 0
if x <> 0 then failwith "fail!" 
        """

[<TestCase(true)>]
[<TestCase(false)>]
let TestEvalSetterOnClass(dyntypes: bool) =
    SimpleTestCase false dyntypes "TestEvalSetterOnClass" """
type C(x: int) = 
    let mutable y = x
    member __.Y with get() = y and set v = y <- v

printfn "initializing..."
let c = C(3)
if c.Y <> 3 then failwithf "fail!, c.Y = %d, expected 3" c.Y
printfn "assigning..."
c.Y <- 4
if c.Y <> 4 then failwith "fail! fail!" 
        """

[<TestCase(true)>]
[<TestCase(false)>]
let TestLengthOnList(dyntypes: bool) =
    SimpleTestCase false dyntypes "TestLengthOnList" """
let x = [1;2;3].Length
if x <> 3 then failwith "fail! fail!" 
        """
[<TestCase(true)>]
[<TestCase(false)>]
let TestEvalLocalFunctionOnClass(dyntypes: bool) =
    SimpleTestCase false dyntypes "TestEvalLocalFunctionOnClass" """
type C(x: int) = 
    let f x = x + 1
    member __.Y with get() = f x

let c = C(3)
if c.Y <> 4 then failwith "fail!" 
        """

[<TestCase(true)>]
[<TestCase(false)>]
let TestEquals(dyntypes: bool) =
    SimpleTestCase false dyntypes "TestEquals" """
let x = (1 = 2)
        """


[<TestCase(true)>]
[<TestCase(false)>]
let TestTypeTest(dyntypes: bool) =
    SimpleTestCase false dyntypes "TestTypeTest" """
let x = match box 1 with :? int as a -> a | _ -> failwith "fail!"
if x <> 1 then failwith "fail fail!" 
        """


[<TestCase(true)>]
[<TestCase(false)>]
let TestTypeTest2(dyntypes: bool) =
    SimpleTestCase false dyntypes "TestTypeTest2" """
let x = match box 2 with :? string as a -> failwith "fail!" | _ -> 1
if x <> 1 then failwith "fail fail!" 
        """

// Known limitation of FSharp Compiler Service
//[<Test>]
//    let GenericThing() =
//    SimpleTestCase false dyntypes "GenericThing" """
//let f () = 
//    let g x = x
//    g 3, g 4, g
//let a, b, (c: int -> int) = f()
//if a <> 3 then failwith "fail!" 
//if b <> 4 then failwith "fail fail!" 
//if c 5 <> 5 then failwith "fail fail fail!" 
//        """

[<TestCase(true)>]
[<TestCase(false)>]
let DateTime(dyntypes: bool) =
    SimpleTestCase false dyntypes "DateTime" """
let v1 = System.DateTime.Now
let v2 = v1.Date
let mutable v3 = System.DateTime.Now
let v4 = v3.Date
        """

[<TestCase(true)>]
[<TestCase(false)>]
let LocalMutation(dyntypes: bool) =
    SimpleTestCase false dyntypes "LocalMutation" """
let f () = 
    let mutable x = 1
    x <- x + 1
    x <- x + 1
    x
if f() <> 3 then failwith "fail fail!" 
        """


[<TestCase(true)>]
[<TestCase(false)>]
let SimpleInheritFromObj(dyntypes: bool) =
    SimpleTestCase false dyntypes "SimpleInheritFromObj" """
type C() =
    inherit obj()
    member val x = 1 with get, set

let c = C()
if c.x <> 1 then failwith "fail fail!" 
c.x <- 3
if c.x <> 3 then failwith "fail fail!" 
        """

[<TestCase(true)>]
[<TestCase(false)>]
let SimpleInheritFromConcreteClass(dyntypes: bool) =
    SimpleTestCase false dyntypes "SimpleInheritFromObj" """
type C() =
    inherit System.Text.ASCIIEncoding()
    member val x = 1 with get, set

let c = C()
if c.CodePage  <> System.Text.ASCIIEncoding().CodePage then failwith "nope"

        """

[<TestCase(true)>]
[<TestCase(false, Ignore= "fails without dynamic emit types")>]
let SimpleInterfaceImpl(dyntypes) =
    SimpleTestCase false dyntypes "SimpleInterfaceImpl" """
open System
type C() =
    interface IComparable with
       member x.CompareTo(y:obj) = 17

let c = C()
let v = (c :> IComparable).CompareTo(c)
if v <> 17 then failwithf "fail fail! expected 17, got %d"  v
        """

[<TestCase(true)>]
[<TestCase(false, Ignore= "fails without dynamic emit types")>]
let SimpleInterfaceDecl(dyntypes) =
    SimpleTestCase false dyntypes "SimpleInterfaceDecl" """

type IComparable2 =
    abstract CompareTo: obj -> int

if typeof<IComparable2>.Name <> "IComparable2" then failwith "bad name"

type C() =
    interface IComparable2 with
       member x.CompareTo(y:obj) = 17

let c = C()
let v = (c :> IComparable2).CompareTo(c)

if v <> 17 then failwithf "fail fail! expected 17, got %d"  v
        """


[<TestCase(true)>]
[<TestCase(false, Ignore= "fails without dynamic emit types")>]
let SimpleAbstractClassDecl(dyntypes) =
    SimpleTestCase false dyntypes "SimpleAbstractClassDecl" """

[<AbstractClass>]
type Comparable2() =
    abstract CompareTo: obj -> int

if typeof<Comparable2>.Name <> "Comparable2" then failwith "bad name"

type C() =
    inherit Comparable2() 
    override x.CompareTo(y:obj) = 17

if typeof<C>.Name <> "C" then failwith "bad name"
let c = C()
let v = c.CompareTo(c)

if v <> 17 then failwithf "fail fail! expected 17, got %d"  v
        """

[<TestCase(true)>]
[<TestCase(false, Ignore= "fails without dynamic emit types")>]
let SimpleInterfaceImpl2(dyntypes) =
    SimpleTestCase false dyntypes "SimpleInterfaceImpl" """
open System.Collections
open System.Collections.Generic
type C() =
    interface IEnumerator with
       member x.MoveNext() = false
       member x.Current = box 1
       member x.Reset() = ()

let c = C() :> IEnumerator
if c.MoveNext() <> false then failwith "fail fail!" 
        """
 
[<TestCase(true, Ignore="fails due to strange reflection emit problem for interface impls in generic classes"); >]
[<TestCase(false, Ignore= "fails without dynamic emit types")>]
let SimpleInterfaceImplGenericClass(dyntypes) =
    SimpleTestCase false dyntypes "SimpleInterfaceImpl" """
open System.Collections
open System.Collections.Generic
type C<'T>() =
    interface IEnumerator with
       member x.MoveNext() = false
       member x.Current = box 1
       member x.Reset() = ()

let c = C<int>() :> IEnumerator
if c.MoveNext() <> false then failwith "fail fail!" 
        """
 
[<TestCase(true)>]
[<TestCase(false, Ignore= "fails without dynamic emit types")>]
let SimpleGenericInterfaceImpl(dyntypes) =
    SimpleTestCase false dyntypes "SimpleGenericInterfaceImpl" """
open System.Collections
open System.Collections.Generic
type C() =
    interface IEnumerator<int> with
       member x.Current = 17
       member x.Dispose() = ()
    interface IEnumerator with
       member x.MoveNext() = false
       member x.Current = box 10
       member x.Reset() = ()

let c = new C() :> IEnumerator<int>
if c.Current <> 17 then failwith "fail fail!" 
if c.Reset() <> () then failwith "fail fail 2!" 
if c.MoveNext() <> false then failwith "fail fail!" 
        """
 
[<TestCase(true)>]
[<TestCase(false, Ignore= "fails without dynamic emit types")>]
let GenericMethodWithConstraint(dyntypes) =
    SimpleTestCase false dyntypes "GenericMethodWithConstraint" """

open System
let f<'T when 'T :> IComparable> (x: 'T) = (x, x)

let (a,b) = f 3.0

if a <> 3.0 then failwith "fail fail!" 
if b <> 3.0 then failwith "fail fail!" 
        """
 
[<TestCase(true)>]
[<TestCase(false)>]
let GenericClassWithConstraint(dyntypes) =
    SimpleTestCase false dyntypes "GenericClassWithConstraint" """

open System
type C<'T when 'T :> IComparable> (x: 'T) = 
    member _.Call() = (x, x)

let (a,b) = C<double>(3.0).Call()

if a <> 3.0 then failwith "fail fail!" 
if b <> 3.0 then failwith "fail fail!" 
        """

[<TestCase(true)>]
[<TestCase(false)>]
let GenericMethodInGenericClassWithConstraint(dyntypes) =
    SimpleTestCase false dyntypes "GenericClassWithConstraint" """

open System
type C<'T when 'T :> IComparable> (x: 'T) = 
    member _.Call<'U when 'U :> IComparable>(y) = (x, y)

let (a,b) = C<double>(3.0).Call<double>(4.0)

if a <> 3.0 then failwith "fail fail!" 
if b <> 4.0 then failwith "fail fail!" 
        """

[<TestCase(true, Ignore= "ignore because union types don't get dynamic emit types yet, codegen is too complicated and FCS doesn't tell us how") >]
[<TestCase(false, Ignore= "fails without dynamic emit types")>]
let UnionTypeWithOverride(dyntypes) =
    SimpleTestCase false dyntypes "UnionTypeWithOverride" """

type UnionType =
   | A
   | B
   override x.ToString() = "dd"

if A.ToString() <> "dd" then failwith "fail fail! 1" 
        """

[<TestCase(true) >]
[<TestCase(false)>]
let SimpleClass(dyntypes) =
    SimpleTestCase false dyntypes "SimpleClass" """

type C(x: int, y: int) =
    member _.X = x
    member _.Y = y
    member _.XY = x + y

let c = C(3,4)
if c.X <> 3 then failwith "fail fail! 1" 
if c.Y <> 4 then failwith "fail fail! 2" 
if c.XY <> 7 then failwith "fail fail! 3" 
        """

[<TestCase(true) >]
[<TestCase(false)>]
let SimpleModule(dyntypes) =
    SimpleTestCase false dyntypes "SimpleClass" """

module M =
    let f x y = printfn "hello"; printfn "hello"; printfn "hello"; printfn "hello"; x + y

let c = M.f 3 4
if c <> 7 then failwith "fail fail! 1" 
        """

[<TestCase(true) >]
//[<TestCase(false)>]
let SimpleClassSelfConstructionNoArguments(dyntypes) =
    SimpleTestCase false dyntypes "SimpleClass" """

type C() =
    member _.X = 1
    member _.Y = 2
    new (x: int, y: int, z: int) = C()

let c = C(3,4,5) // interpretation calls self constructor
if c.X <> 1 then failwith "fail fail! 1" 
if c.Y <> 2 then failwith "fail fail! 2" 
        """

[<TestCase(true) >]
//[<TestCase(false)>]
let SimpleClassSelfConstructionWithArguments(dyntypes) =
    SimpleTestCase false dyntypes "SimpleClass" """

type C(x: int, y: int) =
    member _.X = x
    member _.Y = y
    member _.XY = x + y
    new (x: int, y: int, z: int) = C(x, y)

let c = C(3,4,5) // interpretation calls self constructor
if c.X <> 3 then failwith "fail fail! 1" 
if c.Y <> 4 then failwith "fail fail! 2" 
if c.XY <> 7 then failwith "fail fail! 3" 
        """

[<TestCase(true) >]
[<TestCase(false)>]
let SimpleClassWithInnerFunctions(dyntypes) =
    SimpleTestCase false dyntypes "SimpleClass" """

type C(x: int, y: int) =
    let f x = x + 1
    member _.FX = f x

let c = C(3,4)
if c.FX <> 4 then failwith "fail fail! 4" 
        """

[<TestCase(true) >]
[<TestCase(false)>]
let SimpleStruct(dyntypes) =
    SimpleTestCase false dyntypes "SimpleStruct" """

[<Struct>]
type C(x: int, y: int) =
    member _.X = x
    member _.Y = y
    member _.XY = x + y

let c = C(3,4)
if c.X <> 3 then failwith "fail fail! 1" 
if c.Y <> 4 then failwith "fail fail! 2" 
if c.XY <> 7 then failwith "fail fail! 3" 
        """

[<TestCase(true)>]
[<TestCase(false, Ignore= "fails without dynamic emit types")>]
let SimpleGenericInterfaceImplPassedAsArg(dyntypes) =
    SimpleTestCase false dyntypes "SimpleGenericInterfaceImplPassedAsArg" """
open System.Collections
open System.Collections.Generic
type C() =
    interface IEnumerable with
       member x.GetEnumerator() = (x :> _)
    interface IEnumerable<int> with
       member x.GetEnumerator() = (x :> _)
    interface IEnumerator<int> with
       member x.Current = 1
       member x.Dispose() = ()
    interface IEnumerator with
       member x.MoveNext() = false
       member x.Current = box 1
       member x.Reset() = ()

let c = new C() |> Seq.map id |> Seq.toArray
if c.Length <> 0 then failwith "fail fail!" 
        """
 
[<TestCase(true)>]
[<TestCase(false)>]
let LetRecSmoke(dyntypes: bool) =
    SimpleTestCase false dyntypes "LetRecSmoke" """
let even a = 
    let rec even x = (if x = 0 then true else odd (x-1))
    and odd x = (if x = 0 then false else even (x-1))
    even a

if even 11 then failwith "fail!" 
if not (even 10) then failwith "fail fail!" 
        """

[<TestCase(true)>]
[<TestCase(false)>]
let FastIntegerForLoop(dyntypes: bool) =
    SimpleTestCase false dyntypes "FastIntegerForLoop" """

let f () = 
    let mutable res = 0
    for i in 0 .. 10 do
       res <- res + i
    res

if f() <> List.sum [ 0 .. 10 ] then failwith "fail!" 
        """


[<TestCase(true)>]
[<TestCase(false)>]
let TryGetValueSmoke(dyntypes: bool) =
    SimpleTestCase false dyntypes "TryGetValueSmoke" """
let m = dict  [ (1,"2") ]
let f() = 
    match m.TryGetValue 1 with
    | true, v -> if v <> "2" then failwith "fail!"
    | _ -> failwith "fail2!"

f()
       """

[<TestCase(true)>]
[<TestCase(false)>]
let TestCallUnitFunction(dyntypes: bool) =
    SimpleTestCase false dyntypes "TestCallUnitFunction" """
let theRef = FSharp.Core.LanguagePrimitives.GenericZeroDynamic<int>()
       """


// tests needed:
//   2D arrays
