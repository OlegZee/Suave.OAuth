# Suave.OAuth
A set of WebParts to add OAuth authentication for Suave Web applications. The purpose of OAuth in this library is to allow users of your
application to authorize using their google/twitter/github/... accounts.

[![Build Status](https://travis-ci.org/OlegZee/Suave.OAuth.svg)](https://travis-ci.org/OlegZee/Suave.OAuth)

Currently supports google, facebook and github providers. More providers to go and Twitter is the next one.

## Usage

The following code assumes you are adding google authorization support.

### Add nuget package

Run the following command in Package Manager Console:

    PM> Install-Package Suave.OAuth

### Requesting client_id and client_secret from OAuth providers

Obtain so called client_id and client_secret keys from all providers you are going to support in your application.
E.g for google head to [manage projects](https://console.developers.google.com/project) page, create a new project, navigate
to `Credentials` page, click `Add credentials` and choose `OAuth 2.0 client ID`. Choose `other` and you can skip
specifying redundant info.

### Adding handler to your Suave based application

Now copy both `client id` and `client secret` to the code below.
```fsharp
open Suave.OAuth

let oauthConfigs =
    defineProviderConfigs (function
        | "google" -> fun c ->
            {c with
                client_id = "xxxxxxxxxxxxxxxxxxxxxxxxxxx.apps.googleusercontent.com"
                client_secret = "yyyyyyyyyyyyyyyyyyyyyyy"}
        | "facebook" -> fun c ->
            {c with
                client_id = "xxxxxxxxxxxxxxxxxxxxxxxxxxx.apps.googleusercontent.com"
                client_secret = "yyyyyyyyyyyyyyyyyyyyyyy"}
        | _ -> id   // we do not provide secret keys for other oauth providers
    )
let processLoginUri = "http://localhost:8083/oalogin"
```

Next step is defining two routes as follows:
```fsharp
    path "/oaquery" >=> GET >=> redirectAuthQuery oauthConfigs processLoginUri

    path "/oalogin" >=> GET >=>
        processLogin oauthConfigs processLoginUri
            (fun loginData ->
                // user is authorized and you will likely initialize user session (see Suave.Auth for `authenticated` and such)
                model.logged_in <- true
                model.user_id <- loginData.Id
                model.logged_as <- loginData.Name

                // redirect user to application page
                Redirection.FOUND "/"
            )
            (fun error -> OK <| sprintf "Authorization failed because of `%s`" error)
```

Notice the `processLoginUri` is passed around and it should match the path for second route above. You have to provide your own session management code
as indicated in code above.

### Add login button

Just add one or more button such as "Login via Google" link pointing to "/oalogin?provider=google" endpoint defined above.
Providers supported so far are: 'google', 'github', 'facebook'.

## Notes

You should bind your application session to user id (passed in loginData parameter or login handler), which is stable identifier unlike name, marital status or email.

loginData contains access_token generated for your oauth provider session. However library does not support this key renewal (e.g. Google's one
expires in one hour). Anyway whenever you want to extract more data from provider you should do it right after login.

## Customizing queries
While defining configs you could define:

* provider specific **'scopes'** so that you can request more specific info from provider
* **customize_req**: allows to define specific headers or proxy settings for http webrequest instance

You could also define oauth2 provider not in list:
```fsharp
    let oauthConfigs =
        defineProviderConfigs (
            ...
        )
        // the following code adds "yandex" provider (for demo purposes)
        |> Map.add "yandex"
            {OAuth.EmptyConfig with
                authorize_uri = "https://oauth.yandex.ru/authorize"
                exchange_token_uri = "https://oauth.yandex.ru/token"
                request_info_uri = "https://login.yandex.ru/info"
                scopes = ""
                client_id = "xxxxxxxx"; client_secret = "dddddddd"}
```

# References

   * [Google API](https://developers.google.com/identity/protocols/OAuth2WebServer)
   * [Github API](https://developer.github.com/v3/oauth/)
