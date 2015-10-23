module Suave.OAuth

open Suave
open Suave.Types
open Suave.Http.Applicatives
open Suave.Http
open Suave.Http.Successful
open Suave.Web

type ProviderType = | Google | GitHub | Facebook
type DataEnc = | FormEncode | JsonEncode | Plain

type ProviderConfig = {
    authorize_uri: string;
    exchange_token_uri: string;
    client_id: string;
    client_secret: string
    request_info_uri: string
    scopes: string
    token_response_type: DataEnc
}

let private Empty = {authorize_uri = ""; exchange_token_uri = ""; request_info_uri = ""; client_id = ""; client_secret = ""; scopes = ""; token_response_type = FormEncode}

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
            scopes = "profile"
            token_response_type = JsonEncode
            }
    |> Map.add GitHub
        {Empty with
            authorize_uri = "https://github.com/login/oauth/authorize"
            exchange_token_uri = "https://github.com/login/oauth/access_token"
            request_info_uri = "https://api.github.com/user"
            scopes = ""}
    |> Map.add Facebook
        {Empty with
            authorize_uri = "https://www.facebook.com/dialog/oauth"
            exchange_token_uri = "https://graph.facebook.com/oauth/access_token"
            request_info_uri = "https://graph.facebook.com/me"
            scopes = ""}

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

    let formDecode (s:System.String) =
        s.Split('&') |> Seq.map(fun p -> let [|a;b|] = p.Split('=') in a,b) |> Map.ofSeq

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

let processLogin (config: ProviderConfig) redirectUri (f_success: Map<string,obj> -> WebPart) (f_failure: string -> WebPart) : WebPart =

    let extractToken =
        match config.token_response_type with
        | JsonEncode -> util.parseJsObj >> Map.tryFind "access_token" >> Option.bind (unbox<string> >> Some)
        | FormEncode -> util.formDecode >> Map.tryFind "access_token" >> Option.bind (unbox<string> >> Some)
        | Plain ->      Some

    (fun ctx ->
        ctx.request.queryParam "code" |> printfn "param code: %A" // TODO log

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
                response |> printfn "Auth response is %A"        // TODO log

                return!
                  response |> extractToken
                  |> function
                    | None ->
                        f_failure "failed to extract access token"
                    | Some access_token ->
                        let uri = config.request_info_uri + "?" + (["access_token", access_token] |> util.formEncode)
                        fun ctx -> async {
                            let! response = HttpCli.get uri
                            response |> printfn "/user response %A"        // TODO log

                            let user_info:Map<string,obj> = response |> util.parseJsObj
                            user_info |> printfn "/user_info response %A"  // TODO log

                            return! f_success user_info ctx
                        }
                    <| ctx
            }
        | _ ->
            async {
                return! f_failure "" ctx
            }
    )
