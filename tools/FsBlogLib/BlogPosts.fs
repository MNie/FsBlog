﻿namespace FsBlogLib
open System.IO

// --------------------------------------------------------------------------------------
// Parsing blog posts etc.
// --------------------------------------------------------------------------------------
module BlogPosts = 

  open FileHelpers
  open System.Text.RegularExpressions

  /// Type that stores information about blog posts
  type BlogHeader = 
    { Title : string
      Abstract : string
      Description : string
      Date : System.DateTime
      Url : string
      Tags : seq<string> }

  /// Get all *.cshtml, *.html, *.md and *.fsx files in the blog directory
  let GetBlogFiles blog = seq {
    let exts = set [ ".md"; ".fsx"; ".cshtml"; ".html" ]
    let dirs = Array.append [| blog |] (Directory.GetDirectories(blog))
    for dir in dirs do
      if Path.GetFileNameWithoutExtension(dir) <> "abstracts" then
        for file in Directory.GetFiles(dir) do
          if exts |> Set.contains (Path.GetExtension(file).ToLower()) then
            if Path.GetFileNameWithoutExtension(file) <> "index" then
              yield file }
  
  let scriptHeaderRegex = 
    Regex("^\(\*\@(?<header>[^\*]*)\*\)(?<content>.*)$", RegexOptions.Singleline)
  let razorHeaderRegex = 
    Regex("^\@{(?<header>[^}]*)}(?<content>.*)$", RegexOptions.Singleline)

  /// An FSX file must start with a header (*@ ... *) which is removed 
  /// before Literate processing (and then added back as @{ ... }
  let RemoveScriptHeader ext file = 
    let content = File.ReadAllText(file)
    let reg = (match ext with | ".fsx" -> scriptHeaderRegex | _ -> razorHeaderRegex).Match(content)
    if not reg.Success then 
      failwithf "The following F# script or Markdown file is missing a header:\n%s" file  
    let header = reg.Groups.["header"].Value
    let body = reg.Groups.["content"].Value
    "@{" + header + "}\n", body

  /// Return the header block of any blog post file
  let GetBlogHeaderAndAbstract transformer prefix file =
    let regex =
      match Path.GetExtension(file).ToLower() with
      | ".fsx" -> scriptHeaderRegex
      | ".md" | ".html" | ".cshtml" -> razorHeaderRegex
      | _ -> failwith "File format not supported!"
    let reg = regex.Match(File.ReadAllText(file))
    if not reg.Success then 
      failwithf "The following source file is missing a header:\n%s" file  

    // Read abstract file and transform it
    let abstr = transformer prefix (Path.GetDirectoryName(file) ++ "abstracts" ++ Path.GetFileName(file))
    file, reg.Groups.["header"].Value, abstr

  /// Simple function that parses the header of the blog post. Everybody knows
  /// that doing this with regexes is silly, but the blog post headers are simple enough.
  let ParseBlogHeader renameTag (blog:string) =
    let concatRegex = Regex("\"[\s]*\+[\s]*\"", RegexOptions.Compiled)
    fun (file:string, header:string, abstr) ->
      let lookup =
        header.Split(';')
        |> Array.filter (System.String.IsNullOrWhiteSpace >> not)
        |> Array.map (fun (s:string) -> 
            match s.Trim().Split('=') |> List.ofSeq with
            | key::values -> 
                let value = String.concat "=" values
                key.Trim(), concatRegex.Replace(value.Trim(' ', '\t', '\n', '\r', '"'), "")
            | _ -> failwithf "Invalid header in the following blog file: %s" file ) |> dict
      let relativeFile = file.Substring(blog.Length + 1)
      let relativeFile = let idx = relativeFile.LastIndexOf('.') in relativeFile.Substring(0, idx)
      try
        { Title = lookup.["Title"]
          Url = relativeFile.Replace("\\", "/")
          Abstract = abstr
          Description = lookup.["Description"]
          Tags = lookup.["Tags"].Split([|','|], System.StringSplitOptions.RemoveEmptyEntries) |> Array.map (fun s -> s.Trim() |> renameTag)
          Date = lookup.["Date"] |> System.DateTime.Parse }
      with _ -> failwithf "Invalid header in the following blog file: %s" file

  /// Loads information about all blog posts
  let LoadBlogPosts (tagRenames:System.Collections.Generic.IDictionary<string, string>) transformer blog =
    let renameTag tag = 
      match tagRenames.TryGetValue(tag) with true, s -> s | _ -> tag.ToLower()
    GetBlogFiles blog 
    |> Seq.mapi (fun i v -> 
        GetBlogHeaderAndAbstract transformer (sprintf "abs%d_" i) v 
        |> ParseBlogHeader renameTag blog )
    |> Seq.sortBy (fun b -> b.Date)
    |> Array.ofSeq 
    |> Array.rev
 
  let markdownHeader (date:System.DateTime) title =
     sprintf """@{
    Layout = "post";
    Title = "%s";
    Date = "%s";
    Tags = "";
    Description = "";
}"""    title (date.ToString("yyyy-MM-ddThh:mm:ss"))

  let fsxHeader (date:System.DateTime) title = 
     sprintf """(*@
    Layout = "post";
    Title = "%s";
    Date = "%s";
    Tags = "";
    Description = "";
*)"""   title (date.ToString("yyyy-MM-ddThh:mm:ss"))

  /// News up a file at a specified path/filename with initial content generated
  /// from a header creation function.
  let CreateFile path createHeader ext title = 

    let append a b = sprintf "%s%s" b a
  
    // Perhaps parametize this and bubble it up as a requirement?
    let now = System.DateTime.Now

    let dir = Path.Combine([|path;(sprintf "%i" now.Year)|])
    
    // Maybe use some kind of url formatting callback?
    let filename = 
        Regex.Matches(title, @"\w+")
        |> Seq.cast<Match>
        |> Seq.map (fun m -> m.ToString().ToLower())
        |> Seq.fold (fun s m -> (sprintf "%s-%s" s m)) (sprintf "%s/%s" dir (now.ToString("yyyy-MM-dd")))
        |> append "."
        |> append ext

    EnsureDirectory(dir)
    File.WriteAllText(filename, (createHeader now title))

  /// Creates a new blank markdown post.
  let CreateMarkdownPost path title = 
    CreateFile path markdownHeader "md" title

  /// Creates a new blank fsx post.
  let CreateFsxPost path title = 
    CreateFile path fsxHeader "fsx" title
