### How it works in TIA Portal Openness
In TIA Portal, comments are not just simple strings; they are **multilingual texts**. This means a single tag can have different comments depending on the project's selected language (e.g., English, German, Italian). 

To change a comment programmatically, you don't just assign a string to a `.Comment` property. Instead, you access the tag's multilingual text collection and update the text for a specific language culture.

Here is how you would write that in C#:

```csharp
// Assuming you already found your tag
PlcTag myTag = myTagTable.Tags.Find("300KM150");

if (myTag != null)
{
    // Comments in TIA Portal are a collection of MultilingualTextItems.
    // We look for the comment in a specific language (e.g., English - "en-US" or Italian - "it-IT")
    var commentItem = myTag.Comment.Items.FirstOrDefault(c => c.Culture.Name == "en-US");

    if (commentItem != null)
    {
        // If the comment for this language exists, update it
        commentItem.Text = "Main Conveyor Motor - Updated";
    }
    else
    {
        // If it doesn't exist yet, we create it for that specific language
        myTag.Comment.Items.Create("en-US", "Main Conveyor Motor - Updated");
    }
    
    Console.WriteLine("Comment updated successfully!");
}
```
