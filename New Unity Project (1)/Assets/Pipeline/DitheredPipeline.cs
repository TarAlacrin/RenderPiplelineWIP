using UnityEngine;
using UnityEngine.Rendering;
public class DitheredPipeline : RenderPipeline
{
	RenderTexture shadowMap;
	const string shadowsSoftKeyword = "_SHADOWS_SOFT";
	const string shadowsHardKeyword = "_SHADOWS_HARD";
	const string secondaryVertexLightsKeyword = "_VERTEX_SECONDARY_LIGHTS";

	bool enableDynamicBatching;
	bool gpuInstancing;
	bool secondaryLightsAreVertexLights;

	const int maxVisibleLights = 16;

	static int visibleLightColorsId =
		Shader.PropertyToID("_VisibleLightColors");
	static int visibleLightDirectionsOrPositionsId =
		Shader.PropertyToID("_VisibleLightDirectionsOrPositions");
	static int visibleLightAttenuationsId =
		Shader.PropertyToID("_VisibleLightAttenuations");
	static int visibleLightSpotDirectionsId =
		Shader.PropertyToID("_VisibleLightSpotDirections");
	static int lightIndicesOffsetAndCountID =
		Shader.PropertyToID("unity_LightIndicesOffsetAndCount");

	static int shadowMapId = 
		Shader.PropertyToID("_ShadowMap");
	static int worldToShadowMatricesId =
		Shader.PropertyToID("_WorldToShadowMatrices");
	static int shadowBiasId = 
		Shader.PropertyToID("_ShadowBias");
	static int shadowDataId = 
		Shader.PropertyToID("_ShadowData");
	static int shadowMapSizeId = 
		Shader.PropertyToID("_ShadowMapSize");
	static int globalShadowDataId = 
		Shader.PropertyToID("_GlobalShadowData");



	Vector4[] visibleLightColors = new Vector4[maxVisibleLights];
	Vector4[] visibleLightDirectionsOrPositions = new Vector4[maxVisibleLights];
	Vector4[] visibleLightAttenuations = new Vector4[maxVisibleLights];
	Vector4[] visibleLightSpotDirections = new Vector4[maxVisibleLights];


	CullingResults cull;
	CommandBuffer cameraBuffer = new CommandBuffer { name = "Render Camera" };
	CommandBuffer shadowBuffer = new CommandBuffer { name = "Render Shadows" };

	Vector4[] shadowData = new Vector4[maxVisibleLights];
	Matrix4x4[] worldToShadowMatrices = new Matrix4x4[maxVisibleLights];
	int shadowMapSize;
	int shadowTileCount;
	float shadowDistance;



	public DitheredPipeline(bool dynamicBatching, bool instancing, bool secondaryLightsAreVertexLights, int shadowMapSize, float shadowDistance)
	{
		GraphicsSettings.lightsUseLinearIntensity = true;
		enableDynamicBatching = dynamicBatching;
		gpuInstancing = instancing;
		this.shadowMapSize = shadowMapSize;
		this.secondaryLightsAreVertexLights = secondaryLightsAreVertexLights;
		this.shadowDistance = shadowDistance;
	}

	protected override void Render(ScriptableRenderContext context, Camera[] cameras)
	{
		foreach(Camera cam in cameras)
			Render(context, cam);
	}

	void Render(ScriptableRenderContext context, Camera camera)
	{
		//FRUSTRUM CULL
		ScriptableCullingParameters cullingParams;
		if (!camera.TryGetCullingParameters(out cullingParams))
			return;

		cullingParams.shadowDistance = Mathf.Min(shadowDistance, camera.farClipPlane);//this is used when calculating directional light shadows (specifically the "size" of the directional light's camera)

		//ADD UI TO SCENE VIEW
#if UNITY_EDITOR
		if (camera.cameraType == CameraType.SceneView)
			ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
#endif

		//EXECUTE THE CULL
		cull = context.Cull(ref cullingParams);

		//LIGHTING ARRAY GENERATION
		if (cull.visibleLights.Length > 0)
		{
			ConfigureLights();
			//only run the shadow program if shadows are to be found
			if(shadowTileCount >0)
				RenderShadows(context);
			else
			{
				cameraBuffer.DisableShaderKeyword(shadowsHardKeyword);
				cameraBuffer.DisableShaderKeyword(shadowsSoftKeyword);
			}

			CoreUtils.SetKeyword(cameraBuffer, secondaryVertexLightsKeyword, this.secondaryLightsAreVertexLights);
		}
		else//because I don't configure lights if there are no visible lights, this part clears the lights`
		{
			cameraBuffer.SetGlobalVector(lightIndicesOffsetAndCountID, Vector4.zero);
			cameraBuffer.DisableShaderKeyword(shadowsHardKeyword);
			cameraBuffer.DisableShaderKeyword(shadowsSoftKeyword);
			cameraBuffer.EnableShaderKeyword(secondaryVertexLightsKeyword);
		}

		context.SetupCameraProperties(camera);

		//CLEARFLAGS
		cameraBuffer.ClearRenderTarget(
			(camera.clearFlags & CameraClearFlags.Depth) != 0,
			(camera.clearFlags & CameraClearFlags.Color) != 0,
			camera.backgroundColor
		);

		cameraBuffer.BeginSample("Render Camera");
		cameraBuffer.SetGlobalVectorArray(
			visibleLightColorsId, visibleLightColors
		);
		cameraBuffer.SetGlobalVectorArray(
			visibleLightDirectionsOrPositionsId, visibleLightDirectionsOrPositions
		);
		cameraBuffer.SetGlobalVectorArray(
			visibleLightAttenuationsId, visibleLightAttenuations
		);
		cameraBuffer.SetGlobalVectorArray(
			visibleLightSpotDirectionsId, visibleLightSpotDirections
		);

		context.ExecuteCommandBuffer(cameraBuffer);
		cameraBuffer.Clear();

		//OPAQUES
		var drawSettings = new DrawingSettings(new ShaderTagId("SRPDefaultUnlit"), new SortingSettings(camera))
		{
			enableDynamicBatching = enableDynamicBatching,
			enableInstancing = gpuInstancing
		};

		if (cull.visibleLights.Length > 0)
			drawSettings.perObjectData = PerObjectData.LightData | PerObjectData.LightIndices;



		var filterSettings = new FilteringSettings(RenderQueueRange.opaque);
		context.DrawRenderers(cull, ref drawSettings, ref filterSettings);
		
		//SKYBOX
		context.DrawSkybox(camera);

		//TRANSPARENTS
		SortingSettings transparentSettings = drawSettings.sortingSettings;
		transparentSettings.criteria = SortingCriteria.CommonTransparent;
		drawSettings.sortingSettings = transparentSettings;

		filterSettings.renderQueueRange = RenderQueueRange.transparent;
		context.DrawRenderers(cull, ref drawSettings, ref filterSettings);

		//DEFAULT PIPELINE
#if UNITY_EDITOR
		DrawDefaultPipeline(context, camera);
#endif

		cameraBuffer.EndSample("Render Camera");
		context.ExecuteCommandBuffer(cameraBuffer);
		cameraBuffer.Clear();

		context.Submit();

		//Cleans shadowmap
		if(shadowMap)
		{
			RenderTexture.ReleaseTemporary(shadowMap);
			shadowMap = null;
		}
	}

	Material errorMaterial;


	void DrawDefaultPipeline(ScriptableRenderContext context, Camera camera) {

		if (errorMaterial == null){
			Shader errorShader = Shader.Find("Hidden/InternalErrorShader");
			errorMaterial = new Material(errorShader) {
				hideFlags = HideFlags.HideAndDontSave
			};
		}

		var drawSettings = new DrawingSettings(new ShaderTagId("ForwardBase"), new SortingSettings(camera));


		drawSettings.SetShaderPassName(1, new ShaderTagId("PrepassBase"));
		drawSettings.SetShaderPassName(2, new ShaderTagId("Always"));
		drawSettings.SetShaderPassName(3, new ShaderTagId("Vertex"));
		drawSettings.SetShaderPassName(4, new ShaderTagId("VertexLMRGBM"));
		drawSettings.SetShaderPassName(5, new ShaderTagId("VertexLM"));

	
		drawSettings.overrideMaterial = errorMaterial;

		var filterSettings = new FilteringSettings(RenderQueueRange.all);

		context.DrawRenderers(cull, ref drawSettings, ref filterSettings );
	}


	void ConfigureLights()
	{
		shadowTileCount = 0;
		for (int i=0; i < cull.visibleLights.Length; i++)
		{
			if (i == maxVisibleLights)
				break;

			VisibleLight light = cull.visibleLights[i];
			visibleLightColors[i] = light.finalColor;

			Vector4 attenuation = Vector4.zero;
			attenuation.w = 1f;
			Vector4 shadow = Vector4.zero;
			if (light.lightType == LightType.Directional)
			{
				Vector4 v = light.localToWorldMatrix.GetColumn(2);
				v.x = -v.x;
				v.y = -v.y;
				v.z = -v.z;
				visibleLightDirectionsOrPositions[i] = v;

				shadow = ConfigureShadows(i, light.light);
				shadow.z = 1f;//marks the shadow as being a directional light shadow
			}
			else
			{
				visibleLightDirectionsOrPositions[i] = light.localToWorldMatrix.GetColumn(3);//this is the position for point lights
				attenuation.x = 1f / Mathf.Max(light.range * light.range, 0.00001f);
				

				if(light.lightType == LightType.Spot)
				{
					Vector4 v = light.localToWorldMatrix.GetColumn(2);
					v.x = -v.x;
					v.y = -v.y;
					v.z = -v.z;
					visibleLightSpotDirections[i] = v;


					float outerRad = Mathf.Deg2Rad * 0.5f * light.spotAngle;
					float outerCos = Mathf.Cos(outerRad);
					float outerTan = Mathf.Tan(outerRad);
					float innerCos = Mathf.Cos(Mathf.Atan(((46f / 64f) * outerTan)));

					float angleRange = Mathf.Max(innerCos - outerCos, 0.001f);
					attenuation.z = 1f / angleRange;
					attenuation.w = -outerCos * attenuation.z;

					//shadows
					shadow = ConfigureShadows(i , light.light);
				}
			}

			visibleLightAttenuations[i] = attenuation;
			shadowData[i] = shadow;
		}

		//TODO: READ UP ON NATIVE LISTS AND JUNK
		if (cull.visibleLights.Length > maxVisibleLights)
		{
			var lightIndicies = cull.GetLightIndexMap(Unity.Collections.Allocator.Temp);

			for (int i = maxVisibleLights; i < cull.visibleLights.Length; i++)
			{
				lightIndicies[i] = -1;
			}
			cull.SetLightIndexMap(lightIndicies);
		}

	}

	Vector4 ConfigureShadows(int lightIndex, Light shadowLight)
	{
		//shadows
		Vector4 shadow = Vector4.zero;
		Bounds shadowBounds;
		//if the light has shadows enabled and the shadows are contacting something thats visible
		if (shadowLight.shadows != LightShadows.None && cull.GetShadowCasterBounds(lightIndex, out shadowBounds))
		{
			shadowTileCount += 1;
			shadow.x = shadowLight.shadowStrength;
			shadow.y = shadowLight.shadows == LightShadows.Soft ? 1f : 0f;
		}

		return shadow;
	}




	void RenderShadows(ScriptableRenderContext context)
	{
		//this will adjust the tiling of the shadowmap dynamically, so as to use the maximum amount of texture possible while still tiling multiple shadows togather
		int split;
		if (shadowTileCount <= 1)
			split = 1;
		else if (shadowTileCount <= 4)
			split = 2;
		else if (shadowTileCount <= 9)
			split = 3;
		else
			split = 4;

		//because we support 16 lights, this will tile the shadowmap into 16 portions
		float tileSize = shadowMapSize / split;
		float tileScale = 1f / split;
		Rect tileViewport = new Rect(0f, 0f, tileSize, tileSize);

		shadowMap = RenderTexture.GetTemporary(shadowMapSize, shadowMapSize, 16, RenderTextureFormat.Shadowmap);
		shadowMap.filterMode = FilterMode.Bilinear;
		shadowMap.wrapMode = TextureWrapMode.Clamp;


		CoreUtils.SetRenderTarget(shadowBuffer, shadowMap, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store, ClearFlag.Depth);
		//shadowBuffer.SetRenderTarget(shadowMap, RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);

		shadowBuffer.BeginSample("Render Shadows");
		shadowBuffer.SetGlobalVector(globalShadowDataId, new Vector4(tileScale, shadowDistance*shadowDistance));
		context.ExecuteCommandBuffer(shadowBuffer);
		shadowBuffer.Clear();

		int tileIndex = 0;
		bool hardShadows = false;
		bool softShadows = false;

		for(int i =0; i < cull.visibleLights.Length; i++)
		{
			if (i == maxVisibleLights)//skip this section if the light doesn't have shadows or we've hit the max number of lights
				break;
			if (shadowData[i].x <= 0f)
				continue;

			Matrix4x4 viewMatrix, projectionMatrix;
			ShadowSplitData splitData;

			bool validShadows;
			//because we are essentially rendering the scene from the light's pov, this sets up the spoof VP matricies for the light's perspective. Also checks to see whether it was able to generate a sensible matrix
			if (shadowData[i].z >0f)//if the light is directional
			{
				validShadows = cull.ComputeDirectionalShadowMatricesAndCullingPrimitives(
					i, 0, 1, Vector3.right, (int)tileSize,
					cull.visibleLights[i].light.shadowNearPlane,
					out viewMatrix, out projectionMatrix, out splitData
					);

			}
			else //spotlight
			{
				validShadows = cull.ComputeSpotShadowMatricesAndCullingPrimitives(i, out viewMatrix, out projectionMatrix, out splitData);
			}

			if (!validShadows)
			{
				shadowData[i].x = 0f;
				continue;
			}


			//this is used for tiling 
			float tileOffsetX = tileIndex % split;
			float tileOffsetY = tileIndex / split;
			tileViewport.x = tileOffsetX * tileSize;
			tileViewport.y = tileOffsetY * tileSize;
			shadowData[i].z = tileOffsetX * tileScale;
			shadowData[i].w = tileOffsetY * tileScale;

			shadowBuffer.SetViewport(tileViewport);
			//stops the different tiled shadow maps from cross sampling by adding a border around each one
			shadowBuffer.EnableScissorRect(new Rect(tileViewport.x + 4f, tileViewport.y + 4f, tileSize - 8f, tileSize - 8f));

			shadowBuffer.SetViewProjectionMatrices(viewMatrix, projectionMatrix);
			shadowBuffer.SetGlobalFloat( shadowBiasId, cull.visibleLights[i].light.shadowBias );

			context.ExecuteCommandBuffer(shadowBuffer);
			shadowBuffer.Clear();


			var shadowSettings = new ShadowDrawingSettings(cull, i);
			shadowSettings.splitData = splitData;
			context.DrawShadows(ref shadowSettings);

			if (SystemInfo.usesReversedZBuffer)//some gpus flip clipspace z
			{
				projectionMatrix.m20 = -projectionMatrix.m20;
				projectionMatrix.m21 = -projectionMatrix.m21;
				projectionMatrix.m22 = -projectionMatrix.m22;
				projectionMatrix.m23 = -projectionMatrix.m23;
			}

			//clip space goes from -1 to 1, but texture space goes from 0 to 1, this will imbue that transformation into the matrix
			var scaleOffset = Matrix4x4.identity;
			scaleOffset.m00 = scaleOffset.m11 = scaleOffset.m22 = 0.5f;
			scaleOffset.m03 = scaleOffset.m13 = scaleOffset.m23 = 0.5f;
			worldToShadowMatrices[i] = scaleOffset * (projectionMatrix * viewMatrix);


			tileIndex+=1;//only advance the tileindex when we actually use a tile
			if (shadowData[i].y <= 0)
				hardShadows = true;
			else
				softShadows = true;


		}

		shadowBuffer.DisableScissorRect();

		shadowBuffer.SetGlobalTexture(shadowMapId, shadowMap);
		shadowBuffer.SetGlobalMatrixArray(worldToShadowMatricesId, worldToShadowMatrices);
		shadowBuffer.SetGlobalVectorArray( shadowDataId, shadowData	);

		//Used for soft shadows
		float invShadowMapSize = 1f / shadowMapSize;
		shadowBuffer.SetGlobalVector(shadowMapSizeId, new Vector4(invShadowMapSize, invShadowMapSize, shadowMapSize, shadowMapSize));

		CoreUtils.SetKeyword(shadowBuffer, shadowsHardKeyword, hardShadows);
		CoreUtils.SetKeyword(shadowBuffer, shadowsSoftKeyword, softShadows);

		shadowBuffer.EndSample("Render Shadows");
		context.ExecuteCommandBuffer(shadowBuffer);
		shadowBuffer.Clear();
	}

}
