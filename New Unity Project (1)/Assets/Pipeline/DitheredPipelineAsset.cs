using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(menuName = "Rendering/Dithered Pipeline")]
public class DitheredPipelineAsset : RenderPipelineAsset 
{
	[SerializeField]
	bool dynamicBatching = true;

	[SerializeField]
	bool gpuInstancing = true;

	public enum ShadowMapSize
	{
		_256 = 256,
		_512 = 512,
		_1024 = 1024,
		_2048 = 2048,
		_4096 = 4096
	}
	[SerializeField]
	ShadowMapSize shadowMapSize = ShadowMapSize._1024;


	protected override RenderPipeline CreatePipeline()
	{
		QualitySettings.shadows = ShadowQuality.All;
		return new DitheredPipeline(dynamicBatching, gpuInstancing, (int)shadowMapSize);
	}

}
