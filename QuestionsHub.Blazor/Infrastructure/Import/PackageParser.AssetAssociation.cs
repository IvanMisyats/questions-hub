namespace QuestionsHub.Blazor.Infrastructure.Import;

/// <summary>
/// Asset association methods for linking DOCX images/media to questions.
/// </summary>
public partial class PackageParser
{
    /// <summary>
    /// Associates block assets with handout section when transitioning to answer-related sections.
    /// This ensures assets appearing before answers/comments in the same block are correctly
    /// associated with the handout rather than the comment.
    /// </summary>
    private static void AssociateBlockAssetsBeforeAnswerSection(List<AssetReference> assets, ParserContext ctx)
    {
        foreach (var asset in assets)
        {
            // Skip if already associated
            if (ctx.AssociatedAssetFileNames.Contains(asset.FileName))
                continue;

            if (ctx.HasCurrentQuestion)
            {
                // Force association with pre-answer section (handout or question text)
                var section = ctx.InsideMultilineHandoutBracket
                    ? ParserSection.Handout
                    : ctx.SectionBeforeAnswerRelated;
                AssociateAsset(asset, section, ctx.CurrentQuestion, ctx.Result);
                ctx.AssociatedAssetFileNames.Add(asset.FileName);
            }
            else
            {
                // No question yet - add to pending assets for later association
                ctx.PendingAssets.Add((asset, ctx.SectionBeforeAnswerRelated));
                ctx.AssociatedAssetFileNames.Add(asset.FileName);
            }
        }
    }

    /// <summary>
    /// Associates block assets with the current question or adds them to pending assets.
    /// Skips assets that have already been associated in the current block.
    ///
    /// For multi-question blocks (where QuestionCreatedInCurrentBlock is true and
    /// PreviousQuestionInBlock is set), the logic depends on whether a handout marker
    /// was detected for the current question:
    /// - If handout marker detected: associate with current question's handout
    /// - Otherwise: distribute assets between previous and current questions
    /// </summary>
    private static void AssociateBlockAssets(List<AssetReference> assets, ParserContext ctx)
    {
        // Get unassociated assets
        var unassociatedAssets = assets
            .Where(a => !ctx.AssociatedAssetFileNames.Contains(a.FileName))
            .ToList();

        if (unassociatedAssets.Count == 0)
            return;

        // If we have a current question, associate the assets
        if (ctx.HasCurrentQuestion)
        {
            // When inside a multiline handout bracket, force association with handout section
            var section = ctx.InsideMultilineHandoutBracket ? ParserSection.Handout : ctx.CurrentSection;

            // For multi-question blocks without handout marker for current question:
            // Distribute assets between previous and current questions
            if (ctx.PreviousQuestionInBlock != null && !ctx.HandoutMarkerDetectedInCurrentBlock)
            {
                // First asset goes to previous question (as comment)
                // Remaining assets go to current question (as comment)
                var firstAsset = unassociatedAssets[0];
                AssociateAsset(firstAsset, ParserSection.Comment, ctx.PreviousQuestionInBlock, ctx.Result);
                ctx.AssociatedAssetFileNames.Add(firstAsset.FileName);

                // Associate remaining assets with current question
                foreach (var asset in unassociatedAssets.Skip(1))
                {
                    AssociateAsset(asset, ParserSection.Comment, ctx.CurrentQuestion, ctx.Result);
                    ctx.AssociatedAssetFileNames.Add(asset.FileName);
                }
            }
            else
            {
                // Normal case: associate all assets with current question
                foreach (var asset in unassociatedAssets)
                {
                    AssociateAsset(asset, section, ctx.CurrentQuestion, ctx.Result);
                    ctx.AssociatedAssetFileNames.Add(asset.FileName);
                }
            }
        }
        else
        {
            // No question yet - add to pending assets for later association
            foreach (var asset in unassociatedAssets)
            {
                ctx.PendingAssets.Add((asset, ctx.CurrentSection));
                ctx.AssociatedAssetFileNames.Add(asset.FileName);
            }
        }
    }

    /// <summary>
    /// Flushes pending assets to the current question before transitioning to a new question.
    /// This ensures assets at the end of a question don't leak to the next question.
    /// </summary>
    private static void FlushPendingAssetsToCurrentQuestion(ParserContext ctx)
    {
        if (!ctx.HasCurrentQuestion || ctx.PendingAssets.Count == 0)
            return;

        foreach (var (asset, section) in ctx.PendingAssets)
        {
            // For assets that were pending before we had a question,
            // associate them based on their original section context
            AssociateAsset(asset, section, ctx.CurrentQuestion, ctx.Result);
        }

        ctx.PendingAssets.Clear();
    }

    /// <summary>
    /// Associates remaining unassociated block assets with a specific question.
    /// Called when transitioning to a new question within the same block to ensure
    /// assets belonging to the previous question don't leak to the next one.
    /// Assets are associated as comment assets since question/answer content has been processed.
    /// </summary>
    private static void AssociateRemainingBlockAssetsToQuestion(
        List<AssetReference> assets,
        QuestionDto question,
        ParserContext ctx)
    {
        foreach (var asset in assets)
        {
            // Skip if already associated in this block
            if (ctx.AssociatedAssetFileNames.Contains(asset.FileName))
                continue;

            // Associate as comment asset (since we've processed Q/A content for this question)
            AssociateAsset(asset, ParserSection.Comment, question, ctx.Result);
            ctx.AssociatedAssetFileNames.Add(asset.FileName);
        }
    }

    /// <summary>
    /// Applies pending assets to a newly created question.
    /// These are typically assets that appeared in header/tour sections before any question.
    /// </summary>
    private static void ApplyPendingAssetsToNewQuestion(ParserContext ctx)
    {
        foreach (var (asset, section) in ctx.PendingAssets)
            AssociateAsset(asset, section, ctx.CurrentQuestion, ctx.Result);

        ctx.PendingAssets.Clear();
    }

    private static void AssociateAsset(AssetReference asset, ParserSection section, QuestionDto? question, ParseResult? result = null)
    {
        if (question == null) return;

        // Anything after (or within) answer-related sections counts as comment attachment.
        var isAnswerRelatedSection = section is
            ParserSection.Answer or
            ParserSection.AcceptedAnswers or
            ParserSection.RejectedAnswers or
            ParserSection.Comment or
            ParserSection.Source or
            ParserSection.Authors;

        if (isAnswerRelatedSection)
        {
            if (question.CommentAssetFileName == null)
            {
                question.CommentAssetFileName = asset.FileName;
            }
            else
            {
                // Warn about dropped asset
                result?.Warnings.Add($"Question {question.Number}: extra comment asset ignored: {asset.FileName} (already has: {question.CommentAssetFileName})");
            }
        }
        else
        {
            if (question.HandoutAssetFileName == null)
            {
                question.HandoutAssetFileName = asset.FileName;
            }
            else
            {
                // Warn about dropped asset
                result?.Warnings.Add($"Question {question.Number}: extra handout asset ignored: {asset.FileName} (already has: {question.HandoutAssetFileName})");
            }
        }
    }
}
