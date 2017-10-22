// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.

open Suave
open Suave.Operators
open Suave.Filters
open Suave.Successful
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
    let jss = JavaScriptSerializer()

    let readConfig file =
        (System.Environment.GetEnvironmentVariable("USERPROFILE"), file)
        |> System.IO.Path.Combine
        |> System.IO.File.ReadAllText
        |> jss.DeserializeObject
        |> jso (jso unbox<string>)

[<EntryPoint>]
let main _ =

    let model = {
        name = "olegz"; logged_id = ""; logged_in = false
        provider = ""
        providers = [|"Google"; "GitHub"; "Facebook" |]
        }

    // Here I'm reading my personal API keys from file stored in my %HOME% folder. You will likely define you keys in code (see below).
    let ocfg = Config.readConfig "suave.oauth.config"

    let oauthConfigs =
        defineProviderConfigs (fun pname c ->
            let key = pname.ToLowerInvariant()
            {c with
                client_id = ocfg.[key].["client_id"]
                client_secret = ocfg.[key].["client_secret"]}
        )
        // the following code adds "yandex" provider (for demo purposes)
        |> Map.add "yandex"
            {OAuth.EmptyConfig with
                authorize_uri = "https://oauth.yandex.ru/authorize"
                exchange_token_uri = "https://oauth.yandex.ru/token"
                request_info_uri = "https://login.yandex.ru/info"
                scopes = ""
                client_id = "xxxxxxxx"; client_secret = "dddddddd"}

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
    let app =
        choose [
            path "/" >=> page "main.html" model

            warbler(fun ctx ->
                let authorizeRedirectUri = buildLoginUrl ctx in
                // Note: logon state for current user is stored in global variable, which is ok for demo purposes.
                // in your application you shoud store such kind of data to session data
                OAuth.authorize authorizeRedirectUri oauthConfigs
                    (fun loginData ->

                        model.logged_in <- true
                        model.logged_id <- sprintf "%s (name: %s)" loginData.Id loginData.Name

                        Redirection.FOUND "/"
                    )
                    (fun () ->

                        model.logged_id <- ""
                        model.logged_in <- false

                        Redirection.FOUND "/"
                    )
                    (fun error -> OK <| sprintf "Authorization failed because of `%s`" error.Message)
                )

            OAuth.protectedPart
                (choose [
                    path "/protected" >=> GET >=> OK "You've accessed protected part!"
                ])
                (RequestErrors.FORBIDDEN "You do not have access to that application part (/protected)")

            // we'll never get here
            (OK "Hello World!")
        ]

    startWebServer defaultConfig app
    0
