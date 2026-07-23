module Paths

open System
open System.IO

let ToolName = "Pagefind.Net"
let Repository = "nullean/pagefind-net"
let MainTFM = "net10.0"

let ValidateAssemblyName = false
let IncludeGitHashInInformational = true
let GenerateApiChanges = true

let Root =
    let mutable dir = DirectoryInfo(".")
    while dir.GetFiles("*.slnx").Length = 0 do dir <- dir.Parent
    Environment.CurrentDirectory <- dir.FullName
    dir

let RootRelative path = Path.GetRelativePath(Root.FullName, path)

let Output = DirectoryInfo(Path.Combine(Root.FullName, "build", "output"))

let LibraryProject = DirectoryInfo(Path.Combine(Root.FullName, "src", "Pagefind.Net"))
