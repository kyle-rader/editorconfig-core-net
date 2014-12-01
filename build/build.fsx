// include Fake lib
#r @"tools/FAKE/tools/FakeLib.dll"
open Fake 
open System
open SemVerHelper
open AssemblyInfoFile
open System.Text.RegularExpressions
open System.Linq

// Properties
let buildDir = "build/output/"
let nugetOutDir = "build/output/_packages"

// Default target
Target "Build" (fun _ -> traceHeader "STARTING BUILD")

Target "Clean" (fun _ -> CleanDir buildDir)

Target "BuildApp" (fun _ ->
    let binDirs = !! "src/**/bin/**"
                  |> Seq.map DirectoryName
                  |> Seq.distinct
                  |> Seq.filter (fun f -> f.EndsWith("Debug") || f.EndsWith("Release"))

    CleanDirs binDirs

    //Override the prebuild event because it just calls a fake task BuildApp depends on anyways
    let msbuildProperties = [
      ("Configuration","Release"); 
    ]

    //Compile each csproj and output it seperately in build/output/PROJECTNAME
    !! "src/**/*.csproj"
      |> Seq.map(fun f -> (f, buildDir + directoryInfo(f).Name.Replace(".csproj", "")))
      |> Seq.iter(fun (f,d) -> MSBuild d "Build" msbuildProperties (seq { yield f }) |> ignore)
)

Target "Test" (fun _ ->
    !! (buildDir + "/**/*.Tests.dll") 
      |> NUnit (fun p ->
          {p with
             DisableShadowCopy = true;
             OutputFile = buildDir + "TestResults.xml" }
         )
)

let fileVersion = 
    let assemblyFileContents = ReadFileAsString @"src\EditorConfig.Core\Properties\AssemblyInfo.cs"
    let re = @"\[assembly\: AssemblyFileVersion\(""([^""]+)""\)\]"
    let matches = Regex.Matches(assemblyFileContents,re)
    let defaultVersion = regex_replace re "$1" (matches.Item(0).Captures.Item(0).Value)
    let timestampedVersion = (sprintf "%s-ci%s" defaultVersion (DateTime.UtcNow.ToString("yyyyMMddHHmmss")))
    trace ("timestamped: " + timestampedVersion)
    let fileVersion = (getBuildParamOrDefault "version" timestampedVersion)
    let fv = if isNullOrEmpty fileVersion then timestampedVersion else fileVersion
    trace ("fileVersion: " + fv)
    fv

//CI builds need to be one minor ahead of the whatever we find in our develop branch
let patchedFileVersion = 
    match fileVersion with
    | f when f.Contains("-ci") ->
        let v = regex_replace "-ci.+$" "" f
        let prerelease = regex_replace "^.+-(ci.+)$" "$1" f
        let version = SemVerHelper.parse v
        sprintf "%d.%d.0-%s" version.Major (version.Minor + 1) prerelease
    | _ -> fileVersion

let validateSignedAssembly = fun name ->
    let sn = if isMono then "sn" else "build/tools/sn/sn.exe"
    let out = (ExecProcessAndReturnMessages(fun p ->
                p.FileName <- sn
                p.Arguments <- sprintf @"-v build\output\%s\%s.dll" name name
              ) (TimeSpan.FromMinutes 5.0))

    let valid = (out.ExitCode, out.Messages.FindIndex(fun s -> s.Contains("is valid")))

    match valid with
    | (0, i) when i >= 0 -> trace (sprintf "%s was signed correctly" name) 
    | (_, _) -> failwithf "{0} was not validly signed"
    
    let out = (ExecProcessAndReturnMessages(fun p ->
                p.FileName <- sn
                p.Arguments <- sprintf @"-T build\output\%s\%s.dll" name name
              ) (TimeSpan.FromMinutes 5.0))
    
    let tokenMessage = (out.Messages.Find(fun s -> s.Contains("Public key token is")));
    let token = (tokenMessage.Replace("Public key token is", "")).Trim();

    let valid = (out.ExitCode, token)
    let oficialToken = "96c599bbe3e70f5d"
    match valid with
    | (0, t) when t = oficialToken  -> 
      trace (sprintf "%s was signed with official key token %s" name t) 
    | (_, t) -> traceFAKE "%s was not signed with the official token: %s but %s" name oficialToken t

let nugetPack = fun name ->
    CreateDir nugetOutDir
    let package = (sprintf @"build\%s.nuspec" name)
    let dir = sprintf "%s/%s/" buildDir name
    let nugetOutFile = buildDir + (sprintf "%s/%s.%s.nupkg" name name patchedFileVersion);
    NuGetPack (fun p ->
      {p with 
        Version = patchedFileVersion
        WorkingDir = "build" 
        OutputPath = dir
      })
      package

    MoveFile nugetOutDir nugetOutFile

let suffix = fun (prerelease: PreRelease) -> sprintf "-%s%i" prerelease.Name prerelease.Number.Value
let getAssemblyVersion = (fun _ ->
    let fv = if fileVersion.Contains("-ci") then (regex_replace "-ci.+$" "" fileVersion) else fileVersion
    traceFAKE "patched fileVersion %s" fv
    let version = SemVerHelper.parse fv

    let assemblySuffix = if version.PreRelease.IsSome then suffix version.PreRelease.Value else "";
    let assemblyVersion = sprintf "%i.0.0%s" version.Major assemblySuffix
  
    match (assemblySuffix, version.Minor, version.Patch) with
    | (s, m, p) when s <> "" && s <> "ci" && (m <> 0 || p <> 0)  -> failwithf "Cannot create prereleases for minor or major builds!"
    | ("", _, _) -> traceFAKE "Building fileversion %s for asssembly version %s" fileVersion assemblyVersion
    | _ -> traceFAKE "Building prerelease %s for major assembly version %s " fileVersion assemblyVersion

    assemblyVersion
)

Target "Version" (fun _ ->
  let assemblyVersion = getAssemblyVersion()

  let assemblyDescription = fun (f: string) ->
    let name = f 
    match f.ToLowerInvariant() with
    | f when f = "editorconfig.core" -> "A .NET implementation of the core editorconfig library"
    | f when f = "editorconfig.app" -> "A .NET implementation of the editorconfig tooling"
    | _ -> sprintf "%s" name

  !! "src/**/AssemblyInfo.cs"
    |> Seq.iter(fun f -> 
      let name = (directoryInfo f).Parent.Parent.Name
      CreateCSharpAssemblyInfo f [
        Attribute.Title name
        Attribute.Copyright (sprintf "editorconfig.org %i" DateTime.UtcNow.Year)
        Attribute.Description (assemblyDescription name)
        Attribute.Company "EditorConfig"
        Attribute.Configuration "Release"
        Attribute.Version assemblyVersion
        Attribute.FileVersion patchedFileVersion
        Attribute.InformationalVersion patchedFileVersion
      ]
    )
)


Target "Release" (fun _ -> 
    nugetPack("EditorConfig.App")
    validateSignedAssembly("EditorConfig.App")
)

// Dependencies
"Clean" 
  =?> ("Version", hasBuildParam "version")
  ==> "BuildApp"
  ==> "Test"
  ==> "Build"

"Build"
  ==> "Release"

// start build
RunTargetOrDefault "Build"