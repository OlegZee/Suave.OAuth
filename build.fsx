// xake build file
// boostrapping xake.core
System.Environment.CurrentDirectory <- __SOURCE_DIRECTORY__

let file = System.IO.Path.Combine("packages", "Xake.Core.dll")
if not (System.IO.File.Exists file) then
    printf "downloading xake.core assembly..."; System.IO.Directory.CreateDirectory("packages") |> ignore
    let url = "https://github.com/OlegZee/Xake/releases/download/v0.3.19/Xake.Core.dll"
    use wc = new System.Net.WebClient() in wc.DownloadFile(url, file + "__"); System.IO.File.Move(file + "__", file)
    printfn ""

#r @"packages/Xake.Core.dll"
//#r @"bin/Debug/Xake.Core.dll"

open Xake

let systemClr cmd args =
    let cmd',args' = if Xake.Env.isUnix then "mono", cmd::args else cmd,args
    in system cmd' args'

do xake {ExecOptions.Default with Vars = ["NETFX-TARGET", "4.5"]; FileLog = "build.log"; ConLogLevel = Verbosity.Chatty } {

    rules [
        "all"  => action {
            do! need ["get-deps"]
            do! need ["build"]
            }

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

    ]

}
