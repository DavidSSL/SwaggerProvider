﻿module SwaggerProvider.Tests

open Swagger.Parser.Schema
open Swagger.Parser
open SwaggerProvider.Internal.Compilers
open Expecto
open System
open System.IO
open System.Net.Http

type ThisAssemblyPointer() = class end
let root =
    typeof<ThisAssemblyPointer>.Assembly.Location
    |> Path.GetDirectoryName

let (!) b = Path.Combine(root,b)

let parseJson =
    SwaggerParser.parseJson >> Parsers.parseSwaggerObject

[<Tests>]
let petStoreTests =
  testList "All/Schema" [
    testCase "Schema parse of PetStore.Swagger.json sample (offline)" <| fun _ ->
        let schema =
            !"Schemas/PetStore.Swagger.json"
            |> File.ReadAllText
            |> parseJson
        Expect.equal
            (schema.Definitions.Length)
            6 "only 6 objects in PetStore"

        let expectedInfo =
            {
                Title = "Swagger Petstore"
                Version = "1.0.0"
                Description = "This is a sample server Petstore server.  You can find out more about Swagger at [http://swagger.io](http://swagger.io) or on [irc.freenode.net, #swagger](http://swagger.io/irc/).  For this sample, you can use the api key `special-key` to test the authorization filters."
            }
        Expect.equal (schema.Info) expectedInfo "PetStore schema info"

    testCase "Schema parse of PetStore.Swagger.json sample (online)" <| fun _ ->
        let schema =
            !"Schemas/PetStore.Swagger.json"
            |> File.ReadAllText
            |> parseJson
        Expect.equal
            (schema.Definitions.Length)
            6 "only 6 objects in PetStore"

        use client = new HttpClient()
        let schemaOnline =
            "http://petstore.swagger.io/v2/swagger.json"
            |> client.GetStringAsync
            |> Async.AwaitTask
            |> Async.RunSynchronously
            |> parseJson

        Expect.equal schemaOnline.BasePath schema.BasePath "same BasePath"
        Expect.equal schemaOnline.Host schema.Host "same Host"
        Expect.equal schemaOnline.Info schema.Info "same Info"
        Expect.equal schemaOnline.Schemes schema.Schemes "same allowed schemes"
        Expect.equal schemaOnline.Tags schema.Tags "same tags"
        Expect.equal schemaOnline.Definitions schema.Definitions "same object definitions"
        Expect.equal schemaOnline.Paths schema.Paths "same paths"
        Expect.equal schemaOnline schema "same schema objects"

    testCase "Ensure that parser is able to compose defined and composed properties" <| fun _ ->
        let schema =
            !"Schemas/azure-arm-storage.json"
            |> File.ReadAllText
            |> parseJson
        let (_, obj) =
            schema.Definitions
            |> Array.find (fun (id, _) -> id = "#/definitions/StorageAccount")
        match obj with
        | Object props ->
            let nameExist = props |> Seq.exists (fun x-> x.Name ="name")
            Expect.isTrue nameExist "`Name` property does not found."
        | _ -> failtestf "Expected Object but received %A" obj

    testCase "Parse schema generated by Swashbuckle" <| fun _ ->
        let schema =
            !"Schemas/swashbuckle.json"
            |> File.ReadAllText
            |> parseJson
        let x = schema.Host
        ()
  ]

let parserTestBody formatParser (url:string) =
    let schemaStr = 
        match Uri.TryCreate(url, UriKind.Absolute) with
        | true, uri -> 
            let client = new HttpClient()
            client.GetStringAsync(uri)
            |> Async.AwaitTask
            |> Async.RunSynchronously
        | _ when File.Exists(url) ->
            File.ReadAllText url
        | _ -> 
            failwithf "Cannot find schema '%s'" url

    if not <| System.String.IsNullOrEmpty(schemaStr) then
        let schema = formatParser schemaStr
                     |> Parsers.parseSwaggerObject

        Expect.isGreaterThan
            (schema.Paths.Length + schema.Definitions.Length)
            0 "schema should provide type or operation definitions"

        //Number of generated types may be less than number of type definition in schema
        //TODO: Check if TPs are able to generate aliases like `type RandomInd = int`
        let defCompiler = DefinitionCompiler(schema, false)
        let opCompiler = OperationCompiler(schema, defCompiler, true, false)
        opCompiler.CompileProvidedClients(defCompiler.Namespace)
        ignore <| defCompiler.Namespace.GetProvidedTypes()


let private schemasFromTPTests =
    let folder = Path.Combine(__SOURCE_DIRECTORY__, "../SwaggerProvider.ProviderTests/Schemas")
    Directory.GetFiles(folder)
let JsonSchemasSource =
    Array.concat [schemasFromTPTests; APIsGuru.JsonSchemas] |> List.ofArray
let YamlSchemasSource =
    APIsGuru.YamlSchemas |> List.ofArray

[<Tests>]
let parseJsonSchemaTests =
    JsonSchemasSource
    |> List.map (fun url ->
        testCase
            (sprintf "Parse schema %s" url)
            (fun _ -> parserTestBody SwaggerParser.parseJson url)
       )
    |> testList "Integration/Schema Json Schemas"

[<Tests>]
let parseYamlSchemaTests =
    YamlSchemasSource
    |> List.sort
    |> List.map (fun url ->
        testCase
            (sprintf "Parse schema %s" url)
            (fun _ -> parserTestBody SwaggerParser.parseYaml url)
       )
    |> testList "Integration/Schema Yaml Schemas"
