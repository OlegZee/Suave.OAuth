// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.

open Suave
open Suave.Http.Applicatives
open Suave.Http
open Suave.Http.Successful
open Suave.Web
open Suave.DotLiquid

open Suave.OAuth

type AppModel =
    {
    mutable name: string
    mutable logged_id: string
    mutable logged_in: bool
    mutable provider: string
    mutable providers: string[]
    }

module private Config =

    open System.Web.Script.Serialization
    open System.Collections.Generic

    let KVf f (kv:KeyValuePair<string,_>) = (kv.Key, kv.Value |> f)
    let jso f (d:obj) = ((d :?> IDictionary<string,_>)) |> Seq.map (KVf f) |> Map.ofSeq
    let jss = new JavaScriptSerializer()

    let readConfig file =
        (System.Environment.GetEnvironmentVariable("USERPROFILE"), file)
        |> System.IO.Path.Combine
        |> System.IO.File.ReadAllText
        |> jss.DeserializeObject
        |> jso (jso unbox<string>)

[<EntryPoint>]
let main argv =

    let model = {
        name = "olegz"; logged_id = ""; logged_in = false
        provider = ""
        providers = [|"Google"; "GitHub"; "Facebook" |]
        }

    // Here I'm reading my personal API keys from file stored in my %HOME% folder. You will likely define you keys in code (see below).
    let ocfg = Config.readConfig ".suave.oauth.config"

    let oauthConfigs =
        defineProviderConfigs (fun pname c ->
            let key = pname.ToLowerInvariant()
            {c with
                client_id = ocfg.[key].["client_id"]
                client_secret = ocfg.[key].["client_secret"]}
        )

(*  // you will go that way more likely
    let oauthConfigs =
        defineProviderConfigs (function
            | "google" -> fun c ->
                {c with
                    client_id = "<xxxxxxxxxxxxxx>"
                    client_secret = "<xxxxxxxxxxxxxx>"}
            | "github" -> fun c ->
                {c with
                    client_id = "<xxxxxxxxxxxxxx>"
                    client_secret = "<xxxxxxxxxxxxxx>"}
            | _ -> id    // this application does not define secret keys for other oauth providers
        )
*)

    let processLoginUri = "http://localhost:8083/oalogin"

    let app =
        choose [
            path "/" >>= page "main.html" model

            path "/logout" >>= GET >>= warbler (fun _ ->

                // custom logout logic goes here
                model.logged_id <- ""
                model.logged_in <- false

                Redirection.FOUND "/"
            )

            path "/oaquery" >>= GET >>= OAuth.redirectAuthQuery oauthConfigs processLoginUri

            path "/oalogin" >>= GET >>=
                OAuth.processLogin oauthConfigs processLoginUri
                    (fun user_info ->

                        // TODO pass unified LoginData instead

                        model.logged_in <- true
                        model.logged_id <- sprintf "%s (name: %A)" (user_info.["id"] |> System.Convert.ToString) (user_info.TryFind "name")

                        Redirection.FOUND "/"
                    )
                    (fun error -> OK "Authorization failed")

            (OK "Hello World!")
        ]

    startWebServer defaultConfig app
    0
