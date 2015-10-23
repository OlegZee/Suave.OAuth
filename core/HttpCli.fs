module internal Suave.HttpCli

open System.IO
open System.Net

let post (url : string) (data : byte []) = 
    async { 
        let request = WebRequest.Create(url) :?> HttpWebRequest
        request.Method <- "POST"
        request.ContentType <- "application/x-www-form-urlencoded"
        request.ContentLength <- int64 data.Length

        use stream = request.GetRequestStream()
        stream.Write(data, 0, data.Length)
        stream.Close()
        use! response = request.AsyncGetResponse()
        let stream = response.GetResponseStream()
        use reader = new StreamReader(stream)
        return reader.ReadToEnd()
    }

let get (url : string) = 
    async { 
        let request = WebRequest.Create(url) :?> HttpWebRequest

        request.UserAgent <- "suave app"    // this line is required for github auth

        use! response = request.AsyncGetResponse()
        let stream = response.GetResponseStream()
        use reader = new StreamReader(stream)
        return reader.ReadToEnd()
    }