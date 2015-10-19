module Suave.OAuth

open Suave
open Suave.Types
open Suave.Http.Applicatives
open Suave.Http
open Suave.Http.Successful
open Suave.Web

type ProviderType = | Google | GitHub

type ProviderConfig = {    
    authorize_uri: string;
    exchange_token_uri: string;
    client_id: string;
    client_secret: string
    request_info_uri: string
    scopes: string
}

let private Empty = {authorize_uri = ""; exchange_token_uri = ""; request_info_uri = ""; client_id = ""; client_secret = ""; scopes = ""}

/// <summary>
/// Default (incomplete) oauth provider settings.
/// </summary>
let private providerConfigs =
    Map.empty
    |> Map.add Google
        {Empty with
            authorize_uri = "https://accounts.google.com/o/oauth2/auth"
            exchange_token_uri = "https://www.googleapis.com/oauth2/v3/token"
            request_info_uri = "https://www.googleapis.com/oauth2/v1/userinfo"
            scopes = "email profile"
            }
    |> Map.add GitHub
        {Empty with
            authorize_uri = "https://github.com/login/oauth/authorize"
            exchange_token_uri = "https://github.com/login/oauth/access_token"
            request_info_uri = "https://api.github.com/user"
            scopes = "user:email"}

/// <summary>
/// Allows to completely define provider configs.
/// </summary>
/// <param name="f"></param>
let defineProviderConfigs f = providerConfigs |> Map.map f

module internal util =

    open System.Text
    open System.Web
    open System.Web.Script.Serialization

    let urlEncode = HttpUtility.UrlEncode:string->string
    let asciiEncode = Encoding.ASCII.GetBytes:string -> byte[]
    let utf8Encode = Encoding.UTF8.GetBytes:string -> byte[]

    let formEncode = List.map (fun (k,v) -> String.concat "=" [k; urlEncode v]) >> String.concat "&"

    let parseJsObj js =
        let jss = new JavaScriptSerializer()
        jss.DeserializeObject(js) :?> seq<_> |> Seq.map (|KeyValue|) |> Map.ofSeq

    let stripQuery (uri:System.Uri) : string =
        let i = uri.AbsoluteUri.IndexOf(uri.Query)

        if i > 0 then
            uri.AbsoluteUri.Substring(0, i)
        else
            uri.ToString()

let redirectAuthQuery (config:ProviderConfig) redirectUri : WebPart =
    warbler (fun _ ->

        let parms = [
            "redirect_uri", redirectUri
            "response_type", "code"
            "client_id", config.client_id
            "scope", config.scopes
            ]

        let q = config.authorize_uri + "?" + (parms |> util.formEncode)
        printfn "sending request to a google: %A" q     // TODO convert to a log record
        Redirection.FOUND q
    )

let processLogin (config: ProviderConfig) redirectUri f_success f_failure : WebPart =
    (fun ctx ->                
        ctx.request.queryParam "code" |> printfn "param code: %A"

        match ctx.request.queryParam "code" with
        | Choice1Of2 code ->
            let parms = [
                "code", code
                "client_id", config.client_id
                "client_secret", config.client_secret
                "redirect_uri", redirectUri
                "grant_type", "authorization_code"
            ]

            async {
                let! response = parms |> util.formEncode |> util.asciiEncode |> HttpCli.post config.exchange_token_uri
                let xx = response |> util.parseJsObj
                xx |> printfn "Auth response is %A"        // TODO log

                let access_token = xx.["access_token"] |> unbox<string>

                let uri = config.request_info_uri + "?" + (["access_token", access_token] |> util.formEncode)
                let! response = HttpCli.get uri
                response |> printfn "/user response %A"        // TODO log

                let user_info = response |> util.parseJsObj
                user_info |> printfn "/user_info response %A"  // TODO log

                return! f_success user_info ctx
            }
        | _ ->
            async {
                return! f_failure "" ctx
            }
    )
