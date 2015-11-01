module Suave.OAuth

open Suave
open Suave.Types
open Suave.Http.Applicatives
open Suave.Http
open Suave.Http.Successful
open Suave.Web

type DataEnc = | FormEncode | JsonEncode | Plain

type ProviderConfig = {
    authorize_uri: string;
    exchange_token_uri: string;
    client_id: string;
    client_secret: string
    request_info_uri: string
    scopes: string
    token_response_type: DataEnc
    customize_req: System.Net.HttpWebRequest -> unit
}

exception private OAuthException of string

/// <summary>
/// Result type for successive login callback.
/// </summary>
type LoginData = {ProviderName: string; Id: string; Name: string; AccessToken: string; ProviderData: Map<string,obj>}
type FailureData = {Code: int; Message: string; Info: obj}

let EmptyConfig =
    {authorize_uri = ""; exchange_token_uri = ""; request_info_uri = ""; client_id = ""; client_secret = "";
    scopes = ""; token_response_type = FormEncode; customize_req = ignore}

/// <summary>
/// Default (incomplete) oauth provider settings.
/// </summary>
let private providerConfigs =
    Map.empty
    |> Map.add "google"
        {EmptyConfig with
            authorize_uri = "https://accounts.google.com/o/oauth2/auth"
            exchange_token_uri = "https://www.googleapis.com/oauth2/v3/token"
            request_info_uri = "https://www.googleapis.com/oauth2/v1/userinfo"
            scopes = "profile"
            token_response_type = JsonEncode
            }
    |> Map.add "github"
        {EmptyConfig with
            authorize_uri = "https://github.com/login/oauth/authorize"
            exchange_token_uri = "https://github.com/login/oauth/access_token"
            request_info_uri = "https://api.github.com/user"
            scopes = ""}
    |> Map.add "facebook"
        {EmptyConfig with
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

module private impl =

    let get_config ctx (configs: Map<string,ProviderConfig>) =

        let provider_key =
            match ctx.request.queryParam "provider" with
            |Choice1Of2 code -> code.ToLowerInvariant()
            | _ -> "google"

        match configs.TryFind provider_key with
        | None -> raise (OAuthException "bad provider key in query")
        | Some c -> provider_key, c


    let login (configs: Map<string,ProviderConfig>) redirectUri (f_success: LoginData -> WebPart) : WebPart =

        // TODO use Uri to properly add parameter to redirectUri

        (fun ctx ->
            let provider_key,config = configs |> get_config ctx

            let extractToken =
                match config.token_response_type with
                | JsonEncode -> util.parseJsObj >> Map.tryFind "access_token" >> Option.bind (unbox<string> >> Some)
                | FormEncode -> util.formDecode >> Map.tryFind "access_token" >> Option.bind (unbox<string> >> Some)
                | Plain ->      Some
            ctx.request.queryParam "code" |> printfn "param code: %A" // TODO log

            match ctx.request.queryParam "code" with
            | Choice2Of2 _ ->
                raise (OAuthException "server did not return access code")
            | Choice1Of2 code ->

                let parms = [
                    "code", code
                    "client_id", config.client_id
                    "client_secret", config.client_secret
                    "redirect_uri", redirectUri + "?provider=" + provider_key
                    "grant_type", "authorization_code"
                ]

                async {
                    let! response = parms |> util.formEncode |> util.asciiEncode |> HttpCli.post config.exchange_token_uri config.customize_req
                    response |> printfn "Auth response is %A"        // TODO log

                    let access_token = response |> extractToken

                    if Option.isNone access_token then
                        raise (OAuthException "failed to extract access token")

                    let uri = config.request_info_uri + "?" + (["access_token", Option.get access_token] |> util.formEncode)
                    let! response = HttpCli.get uri config.customize_req
                    response |> printfn "/user response %A"        // TODO log

                    let user_info:Map<string,obj> = response |> util.parseJsObj
                    user_info |> printfn "/user_info response %A"  // TODO log

                    let user_id = user_info.["id"] |> System.Convert.ToString
                    let user_name = user_info.["name"] |> System.Convert.ToString

                    return! f_success {ProviderName = provider_key; Id = user_id; Name = user_name; AccessToken = Option.get access_token; ProviderData = user_info} ctx
                }
        )

/// <summary>
/// Login action handler.
/// </summary>
/// <param name="configs"></param>
/// <param name="redirectUri"></param>
let redirectAuthQuery (configs:Map<string,ProviderConfig>) redirectUri : WebPart =
    warbler (fun ctx ->

        let provider_key,config = configs |> impl.get_config ctx

        let parms = [
            "redirect_uri", redirectUri + "?provider=" + provider_key
            "response_type", "code"
            "client_id", config.client_id
            "scope", config.scopes
            ]

        let q = config.authorize_uri + "?" + (parms |> util.formEncode)
        printfn "sending request: %A" q     // TODO convert to a log record
        Redirection.FOUND q
    )

/// <summary>
/// OAuth login provider handler.
/// </summary>
/// <param name="configs"></param>
/// <param name="redirectUri"></param>
/// <param name="f_success"></param>
/// <param name="f_failure"></param>
let processLogin (configs: Map<string,ProviderConfig>) redirectUri (f_success: LoginData -> WebPart) (f_failure: FailureData -> WebPart) : WebPart =

    fun ctx ->
        async {
            try return! impl.login configs redirectUri f_success ctx
            with
                | OAuthException e -> return! f_failure {FailureData.Code = 1; Message = e; Info = e} ctx
                | e -> return! f_failure {FailureData.Code = 1; Message = e.Message; Info = e} ctx
        }
