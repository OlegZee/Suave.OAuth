// xake build file
// boostrapping xake.core
System.Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

let file = System.IO.Path.Combine("packages", "Xake.Core.dll")
if not (System.IO.File.Exists file) then
    printf "downloading xake.core assembly..."; System.IO.Directory.CreateDirectory("packages") |> ignore
    let url = "https://github.com/OlegZee/Xake/releases/download/v0.5.23/Xake.Core.dll"
    use wc = new System.Net.WebClient() in wc.DownloadFile(url, file + "__"); System.IO.File.Move(file + "__", file)
    printfn ""

#r @"packages/Xake.Core.dll"
//#r @"bin/Debug/Xake.Core.dll"

open Xake

let systemClr cmd args =
    let cmd',args' = if Xake.Env.isUnix then "mono", cmd::args else cmd,args
    in system cmd' args'

let (=?) value deflt = match value with |Some v -> v |None -> deflt

let nuget_exe = "packages/NuGet.CommandLine/tools/NuGet.exe"
let DEF_VER = "0.0.1"

module nuget =
    module private impl =
        let newline = System.Environment.NewLine
        let wrapXml node value = sprintf "<%s>%s</%s>" node value node
        let wrapXmlNl node (value:string) =
            let attrs = value.Split([|newline|], System.StringSplitOptions.None) |> Seq.ofArray
            let content = attrs |> Seq.map ((+) "  ") |> String.concat newline
            sprintf "<%s>%s</%s>" node (newline + content + newline) node
        let toXmlStr (node,value) = wrapXml node value

    open impl

    let dependencies deps =
        "dependencies", newline
        + (deps |> List.map (fun (s,v) -> sprintf """<dependency id="%s" version="%s" />""" s v) |> String.concat newline)
        + newline

    let metadata = List.map toXmlStr >> String.concat newline >> wrapXmlNl "metadata"
    let files = List.map (fun(f,t) -> (f,t) ||> sprintf """<file src="%s" target="%s" />""") >> String.concat newline >> wrapXmlNl "files"
    let target t ff = ff |> List.map (fun file -> file,t)
    let package = String.concat newline >> wrapXmlNl "package" >> ((+) ("<?xml version=\"1.0\"?>" + newline))


do xake {ExecOptions.Default with Vars = ["NETFX-TARGET", "4.5"]; FileLog = "build.log"; ConLogLevel = Verbosity.Loud } {

    rules [
        "all"  <== ["get-deps"; "build"; "nuget-pack"]
        "build" <== ["bin/Suave.OAuth.dll"; "bin/OAuth.Example.exe"]

        "clean" => action {
            do! rm ["bin/*.*"]
        }

        "get-deps" => action {
            let! exit_code = systemClr ".paket/paket.bootstrapper.exe" []
            let! exit_code = systemClr ".paket/paket.exe" ["install"]

            if exit_code <> 0 then
                failwith "Failed to install packages"
        }

        ("bin/FSharp.Core.dll") *> fun outfile -> action {
            do! copyFile "packages/FSharp.Core/lib/net40/FSharp.Core.dll" outfile.FullName
            do! copyFiles ["packages/FSharp.Core/lib/net40/FSharp.Core.*data"] "bin"
        }

        ("bin/Suave.dll") *> fun outfile -> action {
            do! copyFile "packages/Suave/lib/net40/Suave.dll" outfile.FullName
        }
        ("bin/Suave.DotLiquid.dll") *> fun outfile -> action {
            do! copyFile "packages/Suave.DotLiquid/lib/net40/Suave.DotLiquid.dll" outfile.FullName
            do! copyFiles ["packages/DotLiquid/lib/NET40/DotLiquid.dll"] "bin"
        }

        ("bin/main.html") *> fun outfile -> action {
            do! copyFile "example/main.html" outfile.FullName
        }

        "bin/Suave.OAuth.dll" *> fun file -> action {

            // eventually there will be multi-target rule
            let xml = "bin/Suave.OAuth.XML"

            let sources = fileset {
                basedir "core"
                includes "VersionInfo.fs"
                includes "AssemblyInfo.fs"
                includes "HttpCli.fs"
                includes "OAuth.fs"
            }

            do! Fsc {
                FscSettings with
                    Out = file
                    Src = sources
                    Ref = !! "bin/FSharp.Core.dll" + "bin/Suave.dll"
                    RefGlobal = ["mscorlib.dll"; "System.dll"; "System.Core.dll"; "System.Web.dll"; "System.Web.Extensions.dll"]
                    Define = ["TRACE"]
                    CommandArgs = ["--optimize+"; "--warn:3"; "--warnaserror:76"; "--utf8output"; "--doc:" + xml]
            }

        }

        "bin/OAuth.Example.exe" *> fun file -> action {

            do! need ["bin/main.html"]
            do! copyFile "core/App.config" (file.FullName + ".config")

            do! Fsc {
                FscSettings with
                    Out = file
                    Src = !! "example/Program.fs"
                    Ref = !! "bin/FSharp.Core.dll" + "bin/Suave.dll" + "bin/Suave.DotLiquid.dll" + "bin/Suave.OAuth.dll"
                    RefGlobal = ["mscorlib.dll"; "System.dll"; "System.Core.dll"; "System.Web.Extensions.dll"]
                    Define = ["TRACE"]
                    CommandArgs = ["--optimize+"; "--warn:3"; "--warnaserror:76"; "--utf8output"]
            }

        }


        "nuget-pack" => action {

            // some utility stuff

            let libFiles = ["bin/Suave.OAuth.dll"]
            do! need libFiles

            let! ver = getEnv("VER")

            let nuspec =
                nuget.package [
                    nuget.metadata [
                        "id", "Suave.OAuth"
                        "version", ver =? DEF_VER
                        "authors", "OlegZee"
                        "owners", "OlegZee"
                        "projectUrl", "https://github.com/OlegZee/Suave.OAuth"
                        "requireLicenseAcceptance", "false"
                        "description", "OAuth authorization WebParts for Suave WebApp framework"
                        "releaseNotes", "Google and GitHub support"
                        "copyright", sprintf "Copyright %i" System.DateTime.Now.Year
                        "tags", "Suave OAuth"
                        nuget.dependencies [
                            "Suave", "1.0.0"
                        ]
                    ]
                    nuget.files (libFiles |> nuget.target "lib/net40")
                ]

            let nuspec_file = "_.nuspec"
            do System.IO.Directory.CreateDirectory("nupkg") |> ignore
            do System.IO.File.WriteAllText(nuspec_file, nuspec)

            let! exec_code = systemClr nuget_exe ["pack"; nuspec_file; "-OutputDirectory"; "nupkg" ]

            if exec_code <> 0 then failwith "failed to build nuget package"
        }

        "nuget-push" => action {

            do! need ["nuget-pack"]

            let! ver = getEnv("VER")
            let package_name = sprintf "Suave.OAuth.%s.nupkg" (ver =? DEF_VER)

            let! nuget_key = getEnv("NUGET_KEY")
            let! exec_code = systemClr nuget_exe ["push"; "nupkg" </> package_name; nuget_key =? ""; "-Source"; "nuget.org"]
            if exec_code <> 0 then failwith ""
        }
    ]

}
