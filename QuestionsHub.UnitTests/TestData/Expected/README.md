# Expected Results Directory

Place JSON files here with expected parsing results.

## Format

Each JSON file should match the normalized ParseResult structure:

```json
{
  "title": "Пакет питань",
  "preamble": null,
  "editors": ["Редактор Редакторович"],
  "tours": [
    {
      "number": "1",
      "editors": [],
      "preamble": null,
      "questions": [
        {
          "number": "1",
          "text": "Текст питання",
          "answer": "Відповідь",
          "acceptedAnswers": null,
          "rejectedAnswers": null,
          "comment": "Коментар",
          "source": "Джерело",
          "authors": ["Автор"],
          "hostInstructions": null,
          "handoutText": null,
          "hasHandoutAsset": false,
          "hasCommentAsset": false
        }
      ]
    }
  ],
  "totalQuestions": 12,
  "confidence": 1.0
}
```

## Generating Expected Files

1. Add your test DOCX to `../Packages/`
2. In `PackageGoldenTests.cs`, remove `Skip` from `GenerateExpectedFile` test
3. Update the `[InlineData]` with your package filename
4. Run the test
5. Review and adjust the generated JSON if needed
6. Re-add the `Skip` attribute

