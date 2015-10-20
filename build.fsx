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

            let (Filelist file_infos) = !! "bin/Suave.OAuth.dll" |> toFileList ""
            let files = file_infos |> List.map (fun fi -> fi.FullName)

            do! need files

            // some utility stuff
            let newline = System.Environment.NewLine
            let wrapXml node value = sprintf "<%s>%s</%s>" node value node
            let wrapXmlNl node (value:string) =
                let attrs = value.Split([|newline|], System.StringSplitOptions.None) |> Seq.ofArray
                let content = attrs |> Seq.map ((+) "  ") |> String.concat newline
                sprintf "<%s>%s</%s>" node (newline + content + newline) node
            let toXmlStr (node,value) = wrapXml node value

            let dependencies deps =
                "dependencies", newline
                + (deps |> List.map (fun (s,v) -> sprintf """<dependency id="%s" version="%s" />""" s v) |> String.concat newline)
                + newline
                

            let! ver = getEnv("VER")
            let version = ver |> function |Some v -> v |_ -> "0.0.1"

            let metadata = [
                "id", "Suave.OAuth"
                "version", version
                "authors", "OlegZee"
                "owners", "OlegZee"
                "projectUrl", "https://github.com/OlegZee/Suave.OAuth"
                "requireLicenseAcceptance", "false"
                "description", "OAuth authorization WebParts for Suave WebApp framework"
                "releaseNotes", "Google and GitHub support"
                "copyright", sprintf "Copyright %i" System.DateTime.Now.Year
                "tags", "Suave OAuth"
                dependencies [
                    "Suave", "0.32.1"
                ]
            ]

            let xml_header = "<?xml version=\"1.0\"?>" + newline
           
            let fileContent = files |> List.map (sprintf """<file src="%s" target="lib" />""") |> String.concat newline |> wrapXmlNl "files"

            let config_data =
                (metadata |> List.map toXmlStr |> String.concat newline |> wrapXmlNl "metadata") + fileContent
                |> wrapXmlNl "package"
            printfn "%s" config_data

            let nuspec_file = "nupkg" </> "_suave.oauth.nuspec"
            do System.IO.Directory.CreateDirectory("nupkg") |> ignore
            do System.IO.File.WriteAllText(nuspec_file, xml_header + config_data)
            let nuget_exe = "packages/NuGet.CommandLine/tools/NuGet.exe"

            let! exec_code = systemClr nuget_exe ["pack"; nuspec_file; "-OutputDirectory"; "nupkg" ]
            
            if exec_code <> 0 then failwith "failed to build nuget package"
        }

    ]

}
