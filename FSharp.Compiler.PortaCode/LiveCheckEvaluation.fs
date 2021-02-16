// F# LiveChecking processing

namespace FSharp.Compiler.PortaCode

open FSharp.Compiler.PortaCode.CodeModel
open FSharp.Compiler.PortaCode.Interpreter
open System
open System.Reflection
open System.Collections.Generic
open System.IO
open System.Text

type LiveCheckEvaluation(options: string[], dyntypes, collectTooltips, livecheck) =

    let mutable assemblyNameId = 0

    let LISTLIM = 20

    let (|ConsNil|_|) (v: obj) =
        let ty = v.GetType()
        if Reflection.FSharpType.IsUnion(ty) then
            let uc, vs = Reflection.FSharpValue.GetUnionFields(v, ty)
            if uc.DeclaringType.IsGenericType && uc.DeclaringType.GetGenericTypeDefinition() = typedefof<list<int>> then
                 match vs with 
                 | [| a; b |] -> Some (Some(a,b))
                 | [|  |] -> Some (None)
                 | _ -> None
            else None
        else None

    let rec (|List|_|) n (v: obj) =
        if n > LISTLIM then Some []
        else
        match v with 
        | ConsNil (Some (a,List ((+) 1 n) b)) -> Some (a::b)
        | ConsNil None -> Some []
        | _ -> None

    /// Format values resulting from live checking using the interpreter
    let rec formatValue (value: obj) = 
        match value with 
        | null -> "null/None"
        | :? string as s -> sprintf "%A" s
        | value -> 
        let ty = value.GetType()
        match value with 
        | _ when ty.Name = "Tensor" || ty.Name = "Shape" ->
            // TODO: this is a hack for DiffSharp, consider how to generalize it
            value.ToString()
        | _ when Reflection.FSharpType.IsTuple(ty) ->
            let vs = Reflection.FSharpValue.GetTupleFields(value)
            "(" + String.concat "," (Array.map formatValue vs) + ")"
        | _ when Reflection.FSharpType.IsFunction(ty)  ->
            "<func>"
        | _ when ty.IsArray ->
            let value = (value :?> Array)
            if ty.GetArrayRank() = 1 then 
                "[| " + 
                    String.concat "; " 
                       [ for i in 0 .. min LISTLIM (value.GetLength(0) - 1) -> 
                            formatValue (value.GetValue(i)) ] 
                 + (if value.GetLength(0) > LISTLIM then "; ..." else "")
                 + " |]"
            elif ty.GetArrayRank() = 2 then 
                "[| " + 
                    String.concat ";   \n" 
                       [ for i in 0 .. min (LISTLIM/2) (value.GetLength(0) - 1) -> 
                            String.concat ";" 
                               [ for j in 0 .. min (LISTLIM/2) (value.GetLength(1) - 1) -> 
                                   formatValue (value.GetValue(i, j)) ] 
                            + (if value.GetLength(1) > (LISTLIM/2) then "; ..." else "")
                       ]
                  + (if value.GetLength(0) > (LISTLIM/2) then "\n   ...\n" else "\n")
                  + " |]"
            else
                sprintf "array rank %d" value.Rank 
        | _ when Reflection.FSharpType.IsRecord(ty) ->
            let fs = Reflection.FSharpType.GetRecordFields(ty)
            let vs = Reflection.FSharpValue.GetRecordFields(value)
            "{ " + String.concat "; " [| for (f,v) in Array.zip fs vs -> f.Name + "=" + formatValue v |] + " }"
        | List 0 els ->
            "[" + String.concat "; " [| for v in els -> formatValue v |] + (if els.Length >= LISTLIM then "; .." else "") + "]"
        | _ when Reflection.FSharpType.IsUnion(ty) ->
            let uc, vs = Reflection.FSharpValue.GetUnionFields(value, ty)
            uc.Name + "(" + String.concat ", " [| for v in vs -> formatValue v |] + ")"
        | _ when (value :? System.Collections.IEnumerable) ->
            "<seq>"
        | _ ->
            value.ToString() //"unknown value"

    let MAXTOOLTIP = 100

    let formatTooltips tooltips =
        let ranges =  HashSet<DRange>(HashIdentity.Structural)
        let havePreferred = tooltips |> Array.choose (fun (m,_,prefer) -> if prefer then Some m else None) |> Set.ofArray
        [| for (range, lines, prefer)  in tooltips do
            if not (ranges.Contains(range))  && (prefer || not (havePreferred.Contains range)) then 
                ranges.Add(range) |> ignore

                // Format multiple lines of text into a single line in the output file
                let valuesText = 
                    [ for (action, value) in lines do 
                          let action = (if action = "" then "" else action + " ")
                          let valueText = try formatValue value with e -> sprintf "??? (%s)" e.Message
                          let valueText = valueText.Replace("\n", "\\n").Replace("\r", "").Replace("\t", "")
                          let valueText = 
                              if valueText.Length > MAXTOOLTIP then 
                                  valueText.[0 .. MAXTOOLTIP-1] + "..."
                              else   
                                  valueText
                          yield action + valueText ]

                let line = range, valuesText
                yield line |]

    let findLiveCheckAttr (attrs: obj[]) =
          attrs |> Array.tryFind (fun a ->  a.GetType().Name.Contains "CheckAttribute")

    // Invoke the RunChecks member
    let callRunChecks(m: DRange option, attr: obj, target: obj, methLocs: (MethodInfo * obj * (string * int * int * int * int))[]) =
        try 
            protectEval false m (fun () ->
                let loc = defaultArg m { File=""; StartLine=0; StartColumn=0; EndLine=0; EndColumn=0 }
                let args = [| target; box (loc.File, loc.StartLine, loc.StartColumn, loc.EndLine, loc.EndColumn); box methLocs;  |]
                let res = protectInvoke (fun () -> attr.GetType().InvokeMember("RunChecks",BindingFlags.Public ||| BindingFlags.InvokeMethod ||| BindingFlags.Instance, null, attr, args))
                let diags = 
                    match res with 
                    | :? (((* severity *) int * (* number *) int * ((* file *) string * int * int * int * int)[] * (* message *) string)[]) as diags -> diags
                    | _ -> 
                        failwith "incorrect return type from attribute Invoke"
                [| for (severity, number, locstack, msg) in diags do
                      let stack = 
                          [| yield! Option.toList m
                             for (file,sl,sc,el,ec) in locstack do
                                { File=file; StartLine=sl; StartColumn=sc; EndLine=el; EndColumn=ec } |]
                      { Severity=severity
                        Number = number
                        Message = msg 
                        LocationStack = stack }  |])
        with exn -> 
            [| DiagnosticFromException exn |]

    // If a [<LiveCheck>] attribute occurs on a type, then call the RunChecks member on 
    // the attribute type passing the target type as an attribute.
    //
    // When a live checking attribute is attached to a type
    // we expect the attribute type to implement an RunChecks method
    // taking the target type and the location information related
    // to the check for diagnostic production.
    let runEntityDeclLiveChecks(entity:DEntityDef, entityR: ResolvedEntity, methDecls: (DMemberDef * ResolvedMember * DExpr)[]) =
        if livecheck then
            match entityR with 
            | REntity (targetType, _) -> 
                let liveCheckAttr = findLiveCheckAttr (targetType.GetCustomAttributes(true))
                match liveCheckAttr with
                | None  -> [| |]
                | Some attr -> 
                    // Grab the source locations of methods to pass to the checker for better error location reporting
                    let methLocs =
                        [| for (membDef, membR, _membBody) in methDecls do
                            
                            printfn "membDef = %A" membDef
                            printfn "membR = %A" membR
                            match membDef.Range, membR with 
                            | Some m, RMethod (:? MethodInfo as minfo) -> 
                                let liveCheckAttr2 = findLiveCheckAttr (minfo.GetCustomAttributes(true))
                                match liveCheckAttr2 with
                                | None -> ()
                                | Some attr2 ->
                                    (minfo, attr2, (m.File, m.StartLine, m.StartColumn, m.EndLine, m.EndColumn))
                            | _ -> ()
                            |]

                    callRunChecks(entity.Range, attr, targetType, methLocs)

            | _ -> [| |] 
        else [| |]

    // If a [<LiveCheck>] attribute occurs on an F# function declaration or static member in 
    // a non-generic type, then call the RunChecks member on 
    // the attribute type passing the target type as an attribute.
    let runMemberDefnLiveChecks(mdef: DMemberDef, methR: ResolvedMember, _body: DExpr) =
        if livecheck && not mdef.IsInstance then
            match methR with 
            | RMethod (:? MethodInfo as minfo)-> 
                let liveCheckAttr = findLiveCheckAttr (minfo.GetCustomAttributes(true))
                match liveCheckAttr with
                | None  -> [| |]
                | Some attr -> callRunChecks(mdef.Range, attr, minfo, [| |])
            | _ -> [| |] 
        else [| |]

    /// Evaluate the declarations using the interpreter
    member t.EvaluateDecls (fileConvContents: (string * DFile)[]) = 
        let assemblyTable = 
            dict [| for r in options do 
                        //printfn "typeof<obj>.Assembly.Location = %s" typeof<obj>.Assembly.Location
                        if r.StartsWith("-r:") && not (r.Contains(".NETFramework")) && not (r.Contains("Microsoft.NETCore.App")) then 
                            let assemName = r.[3..]
                            //printfn "Script: pre-loading referenced assembly %s " assemName
                            match System.Reflection.Assembly.LoadFrom(assemName) with 
                            | null -> 
                                printfn "Script: failed to pre-load referenced assembly %s " assemName
                            | asm -> 
                                let name = asm.GetName()
                                yield (name.Name, asm) |]

        let assemblyResolver (nm: Reflection.AssemblyName) =  
            match assemblyTable.TryGetValue(nm.Name) with
            | true, res -> res
            | _ -> Reflection.Assembly.Load(nm)
                                        
        let tooltips = ResizeArray()
        let sink =
            if collectTooltips then 
                { new Sink with 

                     member _.NotifyEstablishEntityDecl(entity, entityR, entityDecls) =
                         runEntityDeclLiveChecks(entity, entityR, entityDecls)

                     member _.NotifyEstablishMemberDef(mdef, methR, body) =
                         runMemberDefnLiveChecks(mdef, methR, body)

                     member __.NotifyCallAndReturn(mref, callerRange, mdef, _typeArgs, args, res) = 
                         let paramNames = 
                            match mdef with 
                             | Choice1Of2 minfo -> [| for p in minfo.GetParameters() -> p.Name |]
                             | Choice2Of2 mdef -> [| for p in mdef.Parameters -> p.Name |]
                         let isValue = 
                            match mdef with 
                             | Choice1Of2 minfo -> false
                             | Choice2Of2 mdef -> mdef.IsValue
                         let lines = 
                            [ for (p, arg) in Seq.zip paramNames args do 
                                  yield (sprintf "%s:" p, arg)
                              if isValue then 
                                  yield ("value:", res.Value)
                              else
                                  yield ("return:", res.Value) ]
                         match mdef with 
                         | Choice1Of2 _ -> ()
                         | Choice2Of2 mdef -> 
                             mdef.Range |> Option.iter (fun r -> 
                                 tooltips.Add(r, lines, true))
                         match mref with 
                         | None -> ()
                         | Some mref-> 
                             callerRange |> Option.iter (fun r -> 
                                 tooltips.Add(r, lines, true))

                     member __.NotifyBindValue(vdef, value) = 
                         printfn "%A: vdef.Name = %s, vdef.IsCompilerGenerated = %b" vdef.Range vdef.Name vdef.IsCompilerGenerated
                         if not vdef.IsCompilerGenerated then 
                             vdef.Range |> Option.iter (fun r -> tooltips.Add ((r, [("", value.Value)], false)))

                     member __.NotifySetField(typ, fdef, value) = 
                         // Class fields for implicit constructors are reported as 'compiler generated'
                         //if not fdef.IsCompilerGenerated then 
                         fdef.Range |> Option.iter (fun r -> tooltips.Add ((r, [("", value.Value)], false)))

                     member __.NotifyGetField(typ, fdef, m, value) = 
                         // Class fields for implicit constructors are reported as 'compiler generated'
                         //if not fdef.IsCompilerGenerated then 
                         m |> Option.iter (fun r -> tooltips.Add ((r, [("", value.Value)], false)))

                     member __.NotifyBindLocal(vdef, value) = 
                         if not vdef.IsCompilerGenerated then 
                             vdef.Range |> Option.iter (fun r -> tooltips.Add ((r, [("", value.Value)], false)))

                     member __.NotifyUseLocal(vref, value) = 
                         if not vref.IsCompilerGenerated then 
                             vref.Range |> Option.iter (fun r -> tooltips.Add ((r, [("", value.Value)], false)))
                }
                |> Some
            else  
                None

        assemblyNameId <- assemblyNameId + 1
        let assemblyName = AssemblyName("Eval" + string assemblyNameId)
        let ctxt = EvalContext(assemblyName, dyntypes, assemblyResolver, ?sink=sink)

        let allDecls =
            [| for (_, contents) in fileConvContents do yield! contents.Code |]
        ctxt.AddDecls(allDecls)

        let infos = 
            [| for (sourceFile, ds) in fileConvContents do 
                printfn "evaluating decls.... " 
                let selectMemberDef (membDef: DMemberDef) =
                    let isLiveCheck =
                        membDef.CustomAttributes |> Array.exists (fun attr -> 
                            let (DEntityRef typeName) = attr.AttributeType
                            typeName.Contains "CheckAttribute")
                    (membDef.IsValueDef && not livecheck) || 
                    (isLiveCheck && (membDef.IsValueDef || membDef.Parameters.Length = 0))
                let selectInitAction _ =
                    not livecheck 

                let diags = ctxt.TryEvalDecls (envEmpty, ds.Code, selectMemberDef, selectInitAction)

                let formattedTooltips = tooltips.ToArray() |> formatTooltips
                sourceFile, diags, formattedTooltips |]
        infos

