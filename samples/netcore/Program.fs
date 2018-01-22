// Learn more about F# at http://fsharp.net
// See the 'F# Tutorial' project for more help.

open Suave
open Suave.Operators
open Suave.Filters
open Suave.Successful
open Suave.Web
open Suave.DotLiquid

open Suave.OAuth

open Microsoft.Extensions.Configuration

type AppModel =
    {
    mutable name: string
    mutable logged_id: string
    mutable logged_in: bool
    mutable provider: string
    mutable providers: string[]
    }

[<EntryPoint>]
let main _ =

    let model = {
        name = "olegz"; logged_id = ""; logged_in = false
        provider = ""
        providers = [|"Google"; "GitHub"; "Facebook" |]
        }

    // Here I'm reading my personal API keys from file stored in my %HOME% folder. You will likely define you keys in code (see below).
    let config =
        ConfigurationBuilder().SetBasePath(
            System.Environment.GetEnvironmentVariable("USERPROFILE")
            ).AddJsonFile("suave.oauth.config").Build()

    let oauthConfigs =
        defineProviderConfigs (fun pname c ->
            let key = pname.ToLowerInvariant()
            {c with
                client_id = config.[key + ":client_id"]
                client_secret = config.[key + ":client_secret"]}
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
    // TODO this is only ok if you run sample from example folder using "dotnet run" command
    setTemplatesDir <| System.IO.Directory.GetCurrentDirectory()

    let app =
        choose [
            path "/" >=> page "main.html" model

            warbler(fun ctx ->
                let authorizeRedirectUri = buildLoginUrl(ctx, false) in
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
