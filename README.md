# FSharp.Compiler.PortaCode
The PortaCode F# code format and corresponding interpreter. 

* Used by Fabulous and others.

* Currently distributed by source inclusion, no nuget package yet

* Wet paint, API will change

It's used for the "LiveUpdate" feature of Fabulous, to interpret the Elmish model/view/update application code on-device.

It's not actually necessary on Android since full mono JIT is available. On iOS it appears necessary.

The interpreter may also be useful for other live checking tools, because you get escape the whole complication of actual IL generation, Reflection emit and reflection invoke, and no actual classes et.c are generated. We can also adapt the interpreter over time to do things like report extra information back to the host.

The input code format (PortaCode) is derived from FSharp.Compiler.Service expressions, the code is in this repo. The semantics of interpretation can differ from the semantics of .NET F# code. Perf is not good but in many live check scenarios you're sitting on a base set of DLLs which are regular .NET code and are efficiently invoked.
