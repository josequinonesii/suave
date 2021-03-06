﻿namespace Suave

open System
open System.IO
open Suave.Types
open Suave.Types.Codes
open Suave.Http
open Suave.Http.Files
open Suave.Utils

open RazorEngine

module Razor =

  let private asyncMemoize isValid f =
    let cache = Collections.Concurrent.ConcurrentDictionary<_ , _>()
    fun x ->
      async {
        match cache.TryGetValue(x) with
        | true, res when isValid x res -> return res
        | _ ->
            let! res = f x
            cache.[x] <- res
            return res
      }

  let private loadTemplate template_path =
    async {
      let writeTime = File.GetLastWriteTime(template_path)
      use file = new FileStream(template_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)
      use reader = new StreamReader(file)
      let! razorTemplate = reader.ReadToEndAsync()
      return writeTime, razorTemplate
    }

  let loadTemplateCached = 
    loadTemplate |> asyncMemoize (fun templatePath (lastWrite, _) -> 
      File.GetLastWriteTime(templatePath) <= lastWrite )

  open RazorEngine.Configuration
  open RazorEngine.Templating

  let serviceConfiguration = TemplateServiceConfiguration()
  // generate compiled templates in memory instead of on disk as temporary files
  serviceConfiguration.DisableTempFileLocking <- true
  serviceConfiguration.CachingProvider <- new DefaultCachingProvider(fun t -> ())

  let razorService = RazorEngineService.Create(serviceConfiguration)

  /// razor WebPart
  ///
  /// type Bar = { foo : string }
  ///
  /// let app : WebPart =
  ///   url "/home" >>= razor "home.chtml" { foo = "Bar" }
  ///
  let razor<'a> path (model : 'a) =
    fun r ->
      async {
          let template_path = resolvePath r.runtime.homeDirectory path
          let! writeTime, razorTemplate = loadTemplateCached template_path
          let cacheKey = writeTime.Ticks.ToString() + "_" + template_path
          let content = razorService.RunCompile(razorTemplate, cacheKey, typeof<'a>, model)
          return! Response.response HTTP_200 (UTF8.bytes content) r
        }