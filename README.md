# Suave.OAuth
A set of WebParts to add OAuth authentication for Suave Web applications. The purpose of OAuth in this library is to allow users of your
application to authorize using their google/twitter/github/... accounts.

## Usage

The following code assumes you are adding google authorization support.

First you have to obtain so called client_id and client_secret keys from google. Navigate to [manage projects](https://console.developers.google.com/project)
page, create a new project, navigate to `Credentials` page, click `Add credentials` and choose `OAuth 2.0 client ID`. Choose `other` and you can skip
specifying redundant info.

Now copy both `client id` and `client secret` to the code below.

    open Suave.OAuth

    let oauthConfigs =
        defineProviderConfigs (function
            | Google -> fun c ->
                {c with
                    client_id = "xxxxxxxxxxxxxxxxxxxxxxxxxxx.apps.googleusercontent.com"
                    client_secret = "yyyyyyyyyyyyyyyyyyyyyyy"}
            | _ -> id   // we do not provide secret keys for other oauth providers
        )
    let processLoginUri = "http://localhost:8083/oalogin"

Next step is defining two routes as follows:

    path "/oaquery" >>= GET >>= redirectAuthQuery oauthConfigs.[Google] processLoginUri

    path "/oalogin" >>= GET >>=
        processLogin oauthConfigs.[Google] processLoginUri
            (fun user_info ->
                // user is authorized and you will likely initialize user session (see Suave.Auth for `authenticated` and such)
                model.logged_in <- true
                model.user_id <- user_info.["id"] |> unbox<string>
                model.logged_as <- user_info.["email"] |> unbox<string>

                // redirect user to application page
                Redirection.FOUND "/"
            )
            (fun error -> OK "Authorization failed")

Notice the `processLoginUri` is passed around and it should match the path for second route above. You have to provide your own session management code
as indicated in code above.

# References

    [Google API](https://developers.google.com/identity/protocols/OAuth2WebServer)
    [Github API](https://developer.github.com/v3/oauth/)
