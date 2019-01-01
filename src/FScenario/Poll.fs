﻿namespace FScenario

open System
open System.IO
open System.Threading.Tasks
open Polly
open Polly.Timeout

/// <summary>
/// Type representing the required values to run a polling execution.
/// </summary>
type PollAsync<'a> =
    { PollFunc : (unit -> Async<'a>)
      Filter : ('a -> bool)
      Interval : TimeSpan
      Timeout : TimeSpan
      Message : string } with
      /// <summary>
      /// Creates a polling function that runs the specified function for a period of time until either the predicate succeeds or the expression times out.
      /// </summary>
      static member internal Create f = 
        { PollFunc = f
          Filter = fun _ -> true
          Interval = _5s
          Timeout = _30s
          Message = "Polling doesn't result in any values" }
      /// <summary>
      /// Adds a filtering function to speicfy the required result of the polling.
      /// </summary>
      member x.Until (filter : Func<'a, bool>) = { x with Filter = filter.Invoke }
      /// <summary>
      /// Adds a time period representing the interval in which the polling should happen to the polling sequence.
      /// </summary>
      member x.Every (interval : TimeSpan) = { x with Interval = interval }
      /// <summary>
      /// Adds a time period representing how long the polling should happen before the expression should result in a time-out.
      /// </summary>
      member x.For (timeout : TimeSpan) = { x with Timeout = timeout }
      /// <summary>
      /// Adds a custom error message to show when the polling has been time out.
      /// </summary>
      member x.Error (message : string) = { x with Message = message }

/// <summary>
/// Exposing functions to write reliable polling functions for a testable target.
/// </summary>
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Poll =
    /// <summary>
    /// Creates a polling function that runs the specified function for a period of time until either the predicate succeeds or the expression times out.
    /// </summary>
    let target f = PollAsync<_>.Create f
    
    /// <summary>
    /// Creates a polling function that runs the specified functions in parallel returning the first asynchronous computation whose result is 'Some x' 
    /// for a period of time until either the predicate succeeds or the expression times out.
    /// </summary>
    let targets fs = PollAsync<_>.Create (fun () -> async { 
        return! Seq.map (fun f -> f ()) fs
                |> Async.Choice })

    /// <summary>
    /// Creates a polling function that runs the specified functions in parallel returning the first asynchronous computation whose result is 'Some x' 
    /// for a period of time until either the predicate succeeds or the expression times out.
    /// </summary>
    let target2 f1 f2 = targets [ f1; f2 ]

    /// <summary>
    /// Creates a polling function that runs the specified functions in parallel returning the first asynchronous computation whose result is 'Some x' 
    /// for a period of time until either the predicate succeeds or the expression times out.
    /// </summary>
    let target3 f1 f2 f3 = targets [ f1; f2; f3 ]

    /// <summary>
    /// Adds a filtering function to speicfy the required result of the polling.
    /// </summary>
    let until predicate poll = { poll with Filter = predicate }

    /// <summary>
    /// Adds a time period representing the interval in which the polling should happen to the polling sequence.
    /// </summary>
    let every interval poll = { poll  with Interval = interval }

    /// <summary>
    /// Adds a time period representing how long the polling should happen before the expression should result in a time-out.
    /// </summary>
    let timeout timeout poll = { poll with Timeout = timeout }

    /// <summary>
    /// Adds a custom error message to show when the polling has been time out.
    /// </summary>
    let error message poll = { poll with Message = message }

    /// <summary>
    /// Poll at a given target using a filtering function for a period of time until either the predicate succeeds or the expression times out.
    /// </summary>
    /// <param name="pollFunc">A function to poll on a target, this function will be called once every interval.</param>
    /// <param name="predicate">A filtering function to specify the required result of the polling.</param>
    /// <param name="interval">A time period representing the interval in which the polling should happen.</param>
    /// <param name="timeout">A time period representing how long the polling should happen before the expression should result in a time-out.</param>
    /// <param name="errorMessage">A custom error message to show when the polling has been time out. </param>
    /// <returns>An asynchronous expression that polls periodically for a result that matches the specified predicate.</returns>
    let untilCustom pollFunc predicate interval timeout errorMessage = async {
        let! result = 
            Policy.TimeoutAsync(timeout : TimeSpan)
                  .WrapAsync(
                      Policy.HandleResult(resultPredicate=Func<_, _> (not << predicate))
                            .WaitAndRetryForeverAsync(Func<_, _> (fun _ -> interval)))
                  .ExecuteAndCaptureAsync(Func<Task<_>> (pollFunc >> Async.StartAsTask))
                  |> Async.AwaitTask

        match result.Outcome with
        | OutcomeType.Successful -> 
            return result.Result
        | _ -> match result.FinalException with
               | :? TimeoutRejectedException -> raise (TimeoutException errorMessage)
               | _ -> raise result.FinalException
               return Unchecked.defaultof<_> }

    /// <summary>
    /// Poll at a given target using a filtering function every second for 5 seconds.
    /// </summary>
    /// <param name="pollFunc">A function to poll on a target, this function will be called once every interval.</param>
    /// <param name="predicate">A filtering function to specify the required result of the polling.</param>
    /// <param name="errorMessage">A custom error message to show when the polling has been time out. </param>
    let untilEvery1sFor5s pollFunc predicate errorMessage = 
        untilCustom pollFunc predicate (TimeSpan.FromSeconds 1.) (TimeSpan.FromSeconds 5.) errorMessage

    /// <summary>
    /// Poll at a given target using a filtering function every second for 10 seconds.
    /// </summary>
    /// <param name="pollFunc">A function to poll on a target, this function will be called once every interval.</param>
    /// <param name="predicate">A filtering function to specify the required result of the polling.</param>
    /// <param name="errorMessage">A custom error message to show when the polling has been time out. </param>
    let untilEvery1sFor10s pollFunc predicate errorMessage = 
        untilCustom pollFunc predicate (TimeSpan.FromSeconds 1.) (TimeSpan.FromSeconds 10.) errorMessage

    /// <summary>
    /// Poll at a given target using a filtering function every 5 second for 30 seconds.
    /// </summary>
    /// <param name="pollFunc">A function to poll on a target, this function will be called once every interval.</param>
    /// <param name="predicate">A filtering function to specify the required result of the polling.</param>
    /// <param name="errorMessage">A custom error message to show when the polling has been time out. </param>
    let untilEvery5sFor30s pollFunc predicate errorMessage = 
        untilCustom pollFunc predicate (TimeSpan.FromSeconds 5.) (TimeSpan.FromSeconds 30.) errorMessage

    /// <summary>
    /// Poll at a given file path using a filtering function for a period of time until either the predicate succeeds or the expression times out.
    /// </summary>
    /// <param name="filePath">The file path at which the polling should run to look for the existence of the file.</param>
    /// <param name="predicate">A filtering function to specify the required result of the polling.</param>
    /// <param name="interval">A time period representing the interval in which the polling should happen.</param>
    /// <param name="timeout">A time period representing how long the polling should happen before the expression should result in a time-out.</param>
    /// <param name="errorMessage">A custom error message to show when the polling has been time out. </param>
    let untilFile filePath predicate interval timeout errorMessage =
        untilCustom (fun () -> async { return FileInfo filePath }) (fun f -> f.Exists && predicate f) interval timeout errorMessage

    /// <summary>
    /// Poll at a given file path for a period of time until either the predicate succeeds or the expression times out.
    /// </summary>
    /// <param name="filePath">The file path at which the polling should run to look for the existence of the file.</param>
    /// <param name="interval">A time period representing the interval in which the polling should happen.</param>
    /// <param name="timeout">A time period representing how long the polling should happen before the expression should result in a time-out.</param>
    let untilFileExists filePath interval timeout =
        untilCustom (fun () -> async { return FileInfo filePath }) 
              (fun f -> f.Exists) 
              interval timeout 
              (sprintf "File '%s' is not present after polling (every %A, timeout %A)" filePath interval timeout)

    /// <summary>
    /// Poll at a given file path until the file exists ever second for 5 seconds.
    /// </summary>
    /// <param name="filePath">The file path at which the polling should run to look for the existence of the file.</param>
    let untilFileExistsEvery1sFor5s filePath = untilFileExists filePath _1s _5s

    /// <summary>
    /// Poll at a given file path until the file exists ever second for 10 seconds.
    /// </summary>
    /// <param name="filePath">The file path at which the polling should run to look for the existence of the file.</param>
    let untilFileExistsEvery1sFor10s filePath = untilFileExists filePath _1s _10s

    /// <summary>
    /// Poll at a given file path until the file exists ever 5 seconds for 30 seconds.
    /// </summary>
    /// <param name="filePath">The file path at which the polling should run to look for the existence of the file.</param>
    let untilFileExistsEvery5sFor30s filePath = untilFileExists filePath _5s _30s

    /// <summary>
    /// Poll at a given directory path for a period of time until either the predicate succeeds or the expression times out.
    /// </summary>
    /// <param name="dirPath">The directory path at which the polling should run to look for the existence of the file.</param>
    /// <param name="interval">A time period representing the interval in which the polling should happen.</param>
    /// <param name="timeout">A time period representing how long the polling should happen before the expression should result in a time-out.</param>
    /// <param name="errorMessage">A custom error message to show when the polling has been time out.</param>
    let untilFiles dirPath predicate interval timeout errorMessage =
        untilCustom (fun () -> async { return (DirectoryInfo dirPath).GetFiles () }) predicate interval timeout errorMessage

    /// <summary>
    /// Poll at a given directory path every second for 5 seconds.
    /// </summary>
    /// <param name="dirPath">The directory path at which the polling should run to look for the existence of the file.</param>
    /// <param name="predicate">A filtering function to specify the required result of the polling.</param>
    /// <param name="errorMessage">A custom error message to show when the polling has been time out.</param>
    let untilFilesEvery1sFor5s dirPath predicate errorMessage =
        untilFiles dirPath predicate _1s _5s errorMessage

    /// <summary>
    /// Poll at a given directory path every second for 10 seconds.
    /// </summary>
    /// <param name="dirPath">The directory path at which the polling should run to look for the existence of the file.</param>
    /// <param name="predicate">A filtering function to specify the required result of the polling.</param>
    /// <param name="errorMessage">A custom error message to show when the polling has been time out.</param>
    let untilFilesEvery1sFor10s dirPath predicate errorMessage =
        untilFiles dirPath predicate _1s _10s errorMessage

    /// <summary>
    /// Poll at a given directory path every 5 seconds for 30 seconds.
    /// </summary>
    /// <param name="dirPath">The directory path at which the polling should run to look for the existence of the file.</param>
    /// <param name="predicate">A filtering function to specify the required result of the polling.</param>
    /// <param name="errorMessage">A custom error message to show when the polling has been time out.</param>
    let untilFilesEvery5sFor30s dirPath predicate errorMessage =
        untilFiles dirPath predicate _5s _30s errorMessage

    /// <summary>
    /// Poll at a given HTTP endpoint by sending GET requests every specified interval until either the target response with OK or the expression times out.
    /// </summary>
    /// <param name="url">The HTTP url to which the GET requests should be sent.</param>
    /// <param name="interval">A time period representing the interval in which the polling should happen.</param>
    /// <param name="timeout">A time period representing how long the polling should happen before the expression should result in a time-out.</param>
    let untilHttpOk url interval timeout =
        untilCustom 
            (fun () -> Http.get url) 
            (fun r -> r.StatusCode = OK) 
            interval 
            timeout 
            (sprintf "Target '%s' didn't return HTTP OK after polling (every %A, timeout %A)" url interval timeout)
        |> Async.Ignore

    /// <summary>
    /// Poll at a given HTTP endpoint by sending GET requests every second until either the target response with OK 
    /// or the expression times out after 5 seconds.
    /// </summary>
    /// <param name="url">The HTTP url to which the GET requests should be sent.</param>
    let untilHttpOkEvery1sFor5s url =
        untilHttpOk url _1s _5s

    /// <summary>
    /// Poll at a given HTTP endpoint by sending GET requests every second until either the target response with OK 
    /// or the expression times out after 10 seconds.
    /// </summary>
    /// <param name="url">The HTTP url to which the GET requests should be sent.</param>
    let untilHttpOkEvery1sFor10s url =
        untilHttpOk url _1s _10s

    /// <summary>
    /// Poll at a given HTTP endpoint by sending GET requests every 5 seconds until either the target response with OK 
    /// or the expression times out after 30 seconds.
    /// </summary>
    /// <param name="url">The HTTP url to which the GET requests should be sent.</param>
    let untilHttpOkEvery5sFor30s url =
        untilHttpOk url _5s _30s

 type PollAsync<'a> with
    member x.GetAwaiter () = 
        (Poll.untilCustom x.PollFunc x.Filter x.Interval x.Timeout x.Message 
         |> Async.StartAsTask).GetAwaiter()

[<AutoOpen>]
module PollBuilder =
    open Poll
    type PollBuilder () =
        /// <summary>
        /// Creates a polling function that runs the specified function for a period of time until either the predicate succeeds or the expression times out.
        /// </summary>
        [<CustomOperation("target")>] 
        member __.Target (state, f) = { state with PollFunc = f }
        /// <summary>
        /// Adds a filtering function to speicfy the required result of the polling.
        /// </summary>
        [<CustomOperation("until")>] 
        member __.Until (state, predicate) = { state with Filter = predicate }
        /// <summary>
        /// Adds a time period representing how long the polling should happen before the expression should result in a time-out.
        /// </summary>
        [<CustomOperation("every")>]
        member __.Every (state, interval) = { state with Interval = interval }
        /// <summary>
        /// Adds a time period representing the interval in which the polling should happen to the polling sequence.
        /// </summary>
        [<CustomOperation("timeout")>]
        member __.Timeout (state, timeout) = { state with Timeout = timeout }
        /// <summary>
        /// Adds a custom error message to show when the polling has been time out.
        /// </summary>
        [<CustomOperation("error")>]
        member __.Error(state, message) = { state with Message = message }
        member __.Yield (_) = 
            { PollFunc = (fun () -> async.Return Unchecked.defaultof<_>)
              Filter = (fun _ -> true)
              Interval = _5s
              Timeout = _30s
              Message = "Polling doesn't result in in any values" }

    let internal toAsync (a : PollAsync<_>) =
        untilCustom a.PollFunc a.Filter a.Interval a.Timeout a.Message

    type AsyncBuilder with
        member __.Bind (a : PollAsync<'a>, f : 'a -> Async<'b>) = async {
            let! x = toAsync a
            return! f x }

        member __.Bind (a : PollAsync<'a>, f : unit -> Async<unit>) = async {
            let! _ = toAsync a
            return! f () }

        member __.ReturnFrom (a : PollAsync<'a>) = async {
            let! x = toAsync a
            return x }            

    let poll = new PollBuilder ()