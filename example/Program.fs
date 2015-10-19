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
    mutable logged_as: string
    mutable logged_in: bool
    }

module private Config =

    open System.Web.Script.Serialization
    open System.Collections.Generic

    let KVf f (kv:KeyValuePair<string,_>) = (kv.Key, kv.Value |> f)
    let d2m f (d:obj) = ((d :?> IDictionary<string,_>)) |> Seq.map (KVf f) |> Map.ofSeq

    let readConfig file =
        let config_path = System.IO.Path.Combine(System.Environment.GetEnvironmentVariable("USERPROFILE"), file)
        let config = System.IO.File.ReadAllText(config_path)
        let jss = new JavaScriptSerializer()
        config |> jss.DeserializeObject |> d2m (d2m (unbox<string>))

[<EntryPoint>]
let main argv = 

    let model = {name = "olegz"; logged_as = ""; logged_in = false}
    let oauthProvider = ProviderType.Google

    // Here I'm reading my personal API keys from file stored in my %HOME% folder. You will likely define you keys in code (see below).
    let ocfg = Config.readConfig ".suave.oauth.config"

    let oauthConfigs =
        defineProviderConfigs (function
            | Google -> fun c ->
                {c with
                    client_id = ocfg.["Google"].["client_id"]
                    client_secret = ocfg.["Google"].["client_secret"]}
            | GitHub -> fun c ->
                {c with
                    client_id = ocfg.["GitHub"].["client_id"]
                    client_secret = ocfg.["GitHub"].["client_secret"]}
        )

(*  // you will go that way more likely
    let oauthConfigs =
        defineProviderConfigs (function
            | Google -> fun c ->
                {c with
                    client_id = "<xxxxxxxxxxxxxx>"
                    client_secret = "<xxxxxxxxxxxxxx>"}
            | GitHub -> fun c ->
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
            
            path "/login" >>= GET >>= warbler (fun _ ->
                model.logged_as <- "olegzee"
                model.logged_in <- true
                Redirection.FOUND "/"
            )

            path "/logout" >>= GET >>= warbler (fun _ ->
                model.logged_as <- ""
                model.logged_in <- false
                Redirection.FOUND "/"
            )

            path "/oaquery" >>= GET >>= OAuth.redirectAuthQuery oauthConfigs.[oauthProvider] processLoginUri

            path "/oalogin" >>= GET >>=
                OAuth.processLogin oauthConfigs.[oauthProvider] processLoginUri
                    (fun user_info ->
                        model.logged_in <- true
                        model.logged_as <- (user_info.["email"] |> unbox<string>, user_info.["id"] |> unbox<string>) ||> sprintf "%s (id:%s)"

                        Redirection.FOUND "/"
                    )
                    (fun error -> OK "Authorization failed")
            
            (OK "Hello World!")
        ]

    startWebServer defaultConfig app
    0