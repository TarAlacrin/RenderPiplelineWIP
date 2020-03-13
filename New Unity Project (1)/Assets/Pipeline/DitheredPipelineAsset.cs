using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/Dithered Pipeline")]
public class DitheredPipelineAsset : RenderPipelineAsset 
{
	[SerializeField]
	bool dynamicBatching = true;

	[SerializeField]
	bool gpuInstancing = true;


	protected override RenderPipeline CreatePipeline()
	{
		QualitySettings.shadows = ShadowQuality.All;
		return new DitheredPipeline(dynamicBatching, gpuInstancing);
	}

}
