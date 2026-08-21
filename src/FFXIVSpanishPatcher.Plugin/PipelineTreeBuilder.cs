using FFXIVSpanishPatcher.Pipeline;

namespace FFXIVSpanishPatcher.Plugin;

/// <summary>Runs the shared pipeline on a worker and asks it for an unpacked verified tree.</summary>
public sealed class PipelineTreeBuilder(PatchPipeline pipeline, Func<TreeBuildRequest, PatchRequest> requestFactory)
    : ITranslationTreeBuilder
{
    public Task<PatchResult> BuildAsync(TreeBuildRequest request, CancellationToken cancellationToken)
    {
        var patchRequest = requestFactory(request) with { TreeOutputPath = request.CandidateTreePath };
        return Task.Run(
            () => pipeline.Run(patchRequest, cancellationToken: cancellationToken),
            cancellationToken);
    }
}
