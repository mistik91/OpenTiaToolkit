### The Algorithm

1. **Read all existing tags from TIA Portal:** For each tag, use Regex to split the name into `PageNumber` and `BaseName`.
2. **Apply your Business Rule:** 
   * If `PageNumber >= 100`, store it in a dictionary using the `BaseName` as the key (e.g., Key: `"MR100"`, Value: the TIA Tag Object).
   * If `PageNumber < 100`, store it using the *full name* as the key (e.g., Key: `"23F1"`), since you mentioned they only change addresses, not names.
3. **Read your Excel List:** Loop through the Excel rows, split the Excel names using the exact same logic.
4. **Match and Update:** Look up the Excel `BaseName` in your TIA dictionary. If it finds a match, rename the tag, update the address, and update the comment. TIA Portal handles the ladder logic refactoring automatically.
5. **Create New:** If it doesn't find a match, it creates a brand new tag.

### C# Code Example

Here is a conceptual snippet showing how to implement your exact rules:

```csharp
using System;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Siemens.Engineering.SW.Tags;

public class TagUpdater
{
    public static void SyncTagsWithExcel(PlcTagTable tagTable, List<ExcelRow> excelData)
    {
        // Regex to extract leading numbers (Page) and the rest of the string (Base Name)
        // Example: "173MR100" -> Group 1: "173", Group 2: "MR100"
        Regex tagPattern = new Regex(@"^(\d+)([a-zA-Z0-9_]+)$");

        // Dictionaries to hold our existing TIA tags for quick lookup
        var tagsOverPage100 = new Dictionary<string, PlcTag>(); // Key: BaseName (e.g., "MR100")
        var tagsUnderPage100 = new Dictionary<string, PlcTag>(); // Key: FullName (e.g., "23F1")

        // 1. READ EXISTING TIA PORTAL TAGS AND CATEGORIZE THEM
        foreach (PlcTag existingTag in tagTable.Tags)
        {
            Match match = tagPattern.Match(existingTag.Name);
            if (match.Success)
            {
                int pageNumber = int.Parse(match.Groups[1].Value);
                string baseName = match.Groups[2].Value;

                if (pageNumber >= 100)
                {
                    // Store by Base Name (MR100)
                    tagsOverPage100[baseName] = existingTag; 
                }
                else
                {
                    // Store by Full Name (23F1)
                    tagsUnderPage100[existingTag.Name] = existingTag;
                }
            }
        }

        // 2. LOOP THROUGH EXCEL DATA AND UPDATE/CREATE
        foreach (var excelRow in excelData)
        {
            Match match = tagPattern.Match(excelRow.NewTagName); // e.g., "243MR100"
            if (match.Success)
            {
                int excelPageNumber = int.Parse(match.Groups[1].Value);
                string excelBaseName = match.Groups[2].Value; // e.g., "MR100"

                if (excelPageNumber >= 100)
                {
                    // RULE 1: ITEM IS OVER PAGE 100 (Can change page/name AND address)
                    if (tagsOverPage100.ContainsKey(excelBaseName))
                    {
                        PlcTag tagToUpdate = tagsOverPage100[excelBaseName];
                        
                        // Rename the tag (e.g. 173MR100 becomes 243MR100). Ladder logic updates automatically!
                        tagToUpdate.Name = excelRow.NewTagName; 
                        tagToUpdate.LogicalAddress = excelRow.NewAddress;
                        
                        Console.WriteLine($"Updated: shifted {excelBaseName} to page {excelPageNumber}");
                    }
                    else
                    {
                        // Base name didn't exist, this is a completely new motor/sensor
                        tagTable.Tags.Create(excelRow.NewTagName, excelRow.DataType, excelRow.NewAddress);
                        Console.WriteLine($"Created new tag: {excelRow.NewTagName}");
                    }
                }
                else
                {
                    // RULE 2: ITEM IS UNDER PAGE 100 (Only changes address)
                    if (tagsUnderPage100.ContainsKey(excelRow.NewTagName))
                    {
                        PlcTag tagToUpdate = tagsUnderPage100[excelRow.NewTagName];
                        tagToUpdate.LogicalAddress = excelRow.NewAddress;
                        
                        Console.WriteLine($"Updated address for static item: {excelRow.NewTagName}");
                    }
                    else
                    {
                        tagTable.Tags.Create(excelRow.NewTagName, excelRow.DataType, excelRow.NewAddress);
                    }
                }
            }
        }
    }
}

// Dummy class to represent your parsed Excel data
public class ExcelRow
{
    public string NewTagName { get; set; }
    public string NewAddress { get; set; }
    public string DataType { get; set; }
}
```

### Why this approach is incredibly robust:
1. **It acts exactly like a human:** A human engineer looks at `243MR100` and thinks, *"Ah, that's MR100, let me search for where MR100 was in the old project."* This code does exactly that, but in milliseconds.
2. **You don't break the ladder logic:** Because you are finding the *exact Openness Tag Object* representing `173MR100` and changing its `.Name` property, TIA Portal registers it as a rename event, not a delete/create event. All networks referencing that tag instantly update.
3. **Easily expandable:** If your company introduces a new drawing rule (e.g., "Valves are now prefixed with a 'V' instead of 'Y'"), you can simply add a string replacement rule in C# before you do the dictionary lookup. 

Building this into a CLI tool will save you countless hours of manual Excel-to-TIA matching.