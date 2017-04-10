#r @"C:\projects\Suave.OAuth\packages\Suave\lib\net40\Suave.dll"

open Suave

let json = "{\"id\":\"13\", \"value\": true}"
let someJs = System.Text.Encoding.UTF8.GetBytes json
let o = Json.fromJson<System.Collections.Generic.Dictionary<string,string>> someJs

printfn "%A" o