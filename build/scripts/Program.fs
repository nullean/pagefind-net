module Program

open System.IO
open Nullean.Make.Fs
open Proc.Fs

/// The build's targets; `./build.sh --help` lists them with these descriptions
type Target =
    /// clean known output locations
    | Clean
    /// Run build
    | Build
    /// Runs build then tests
    | Test
    /// Pack the NuGet packages into build/output
    | Generatepackages
    /// Validate the packed NuGet packages
    | Validatepackages
    /// Report public API changes against the previous release
    | Generateapichanges
    /// Build documentation and serve locally for preview
    | Docs

/// build/output, relative to the repository root
let private output = Paths.RootRelative Paths.Output.FullName

/// The packed .nupkg files, newest first
let private packages () =
    Paths.Output.GetFiles("*.nupkg") |> Seq.sortByDescending (fun f -> f.CreationTimeUtc) |> Seq.toList

[<EntryPoint>]
let main argv =
    let app = MakeApp<Target>("./build.sh", None)
    let port = app.Option<int>("--port", desc = "Port to use when serving documentation (default: 8000)", defaultValue = 8000)

    // `-s` skips what a target `requires`, never what it `composes`: build always cleans first
    app.Bind <| function
        | Clean -> target [] {
              if Paths.Output.Exists then Paths.Output.Delete true
              sh $"dotnet clean" }
        | Build -> command {
              composes [Clean]
              executes (shell { sh $"dotnet build -c Release" }) }
        | Test -> target [Build] {
              sh $"dotnet run --project tests/Pagefind.Net.Tests/Pagefind.Net.Tests.csproj -c Release"
              sh $"dotnet run --project tests/Pagefind.Net.E2E.Tests/Pagefind.Net.E2E.Tests.csproj -c Release" }
        | Generatepackages -> target [] { sh $"dotnet pack -c Release -o {output}" }
        | Validatepackages -> target [] {
              let version = Versions.informational.Value
              for package in packages () do
                  let package = Paths.RootRelative package.FullName
                  sh $"dotnet nupkg-validator validate {package} -v {version} -t {output}" }
        | Generateapichanges -> target [] {
              let version = Versions.current.Value
              for package in packages () do
                  let id = Path.GetFileNameWithoutExtension(package.Name).Replace("." + version, "")
                  printfn "(%s) -> (%s)" id version
                  let report = Path.Combine(output, $"breaking-changes-{id}.md")
                  // quoted: an unquoted | would pipe
                  sh $"dotnet assembly-differ diff 'previous-nuget|{id}|{version}|{Paths.MainTFM}' 'directory|.artifacts/bin/{id}/release' -a --target {id} -f github-comment --output {report}" }
        | Docs -> target [] {
              Documentation.build ()
              Documentation.serve port.Value }

    app.RunAsync(argv).GetAwaiter().GetResult()
