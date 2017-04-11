module internal Suave.HttpCli

open System.IO
open System.Net.Http

let private httpClient = new HttpClient()
httpClient.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Suave App") |> ignore

type DefineRequest = HttpRequestMessage -> unit

let send (url : string) methd (define : DefineRequest) = 
    async {
        use request = new HttpRequestMessage(methd, url)
        do define request

        let! response = httpClient.SendAsync request |> Async.AwaitTask
        response.EnsureSuccessStatusCode() |> ignore

        let! responseBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask
        return responseBody
    }

let post (url : string) (define : DefineRequest) (data : byte []) = 
    async {
        use request = new HttpRequestMessage(HttpMethod.Post, url)
        request.Content <- new ByteArrayContent(data)
        do define request

        let! response = httpClient.SendAsync request |> Async.AwaitTask
        response.EnsureSuccessStatusCode() |> ignore

        let! responseBody = response.Content.ReadAsStringAsync() |> Async.AwaitTask
        return responseBody
    }

let get (url : string) = send url HttpMethod.Get
