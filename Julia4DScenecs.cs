using Engine.AssetManagement;
using Engine.BackEnd;
using OpenTK;
using OpenTK.Mathematics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Julia4D
{
    public class Julia4DScenecs
    {
        private static TextureFormats SSAO_Format = TextureFormats.R8_Unorm;
        private static int ssao_blur_pixels_per_threadgroups = 32;
        private static int blockSizeX = 4;
        private static int blockSizeY = 4;
        private static int blockSizeZ = 2;

        private static bool BuildAS = true;
        private static int AccelerationStructureSize = 64;

        internal static void UpdateResolution(Processor processor, int resolution_sq)
        {
            int resolution = (int)Math.Pow(2, resolution_sq);
            int computeGroupsX = resolution / blockSizeX;
            int computeGroupsY = resolution / blockSizeY;
            int computeGroupsZ = resolution / blockSizeZ;

            processor.AddCommand(new CreateBuffer("julia4DResult", (long)resolution * resolution * (resolution / 8)));
            processor.AddCommand(new SetComputePassThreadGroupsCommand("pass_voxelize", computeGroupsX, computeGroupsY, computeGroupsZ));

            processor.AddCommand(new SetComputeShaderUniformCommand<float>("u_resolution_f", (float)resolution));
            processor.AddCommand(new SetComputeShaderUniformCommand<int>("u_resolution_i", resolution));
            processor.AddCommand(new SetUniformCommand<float>("normedVoxelSize", 1.0f / ((float)resolution)));

            processor.AddCommand(new SetUniformCommand<float>("u_resolution_f", (float)resolution));
            processor.AddCommand(new SetUniformCommand<int>("u_resolution_i", resolution));

            RecalculateFractal(processor);
        }

        internal static void RecalculateFractal(Processor processor)
        {
            processor.AddCommand(new IncrementRenderPass("pass_voxelize"));
            
            if(BuildAS)
            {
                processor.AddCommand(new IncrementRenderPass("pass_build_as"));
            }
        }

        private static int CalculateSSAOBlurThreadgroups(int screen_size)
        {
            return (screen_size + ssao_blur_pixels_per_threadgroups - 1) / ssao_blur_pixels_per_threadgroups;
        }

        internal static void ResizeFramebufer(Processor processor, int width, int height, float screen_resolution_percentage = 1.0f)
        {
            int scaled_width = (int)(width * screen_resolution_percentage);
            int scaled_height = (int)(height * screen_resolution_percentage);
            
            processor.AddCommand(new ResizeRenderPassTextureCommand("pass_depth_render", scaled_width, scaled_height));
            processor.AddCommand(new ResizeRenderPassTextureCommand("ssaopass", scaled_width, scaled_height));

            //replaces the old texture
            processor.AddCommand(new Create2DTextureCommand("ssao_blur_temp", scaled_width, scaled_height, SSAO_Format));
            processor.AddCommand(new SetComputePassThreadGroupsCommand("blurSSAOH", CalculateSSAOBlurThreadgroups(scaled_width), CalculateSSAOBlurThreadgroups(scaled_height), 1));
            processor.AddCommand(new SetComputePassThreadGroupsCommand("blurSSAOV", CalculateSSAOBlurThreadgroups(scaled_width), CalculateSSAOBlurThreadgroups(scaled_height), 1));
        }

        public static void SetupSSAO(Processor processor, string depth_texture_name, string normal_texture_name, int order, int width, int height, string macros)
        {
            processor.AddCommand(new CreateComputeShaderCommand("gaussianH_ssao", new AssetUri("asset:///Shaders/gaussian.comp"), "#define HORIZONTAL\n#define FORMAT r8\n#define KERNEL_RADIUS 5"));
            processor.AddCommand(new CreateComputeShaderCommand("gaussianV_ssao", new AssetUri("asset:///Shaders/gaussian.comp"), "#define VERTICAL\n#define FORMAT r8\n#define KERNEL_RADIUS 5"));
            processor.AddCommand(new CreateShaderCommand("ssao_shader", new AssetUri("asset:///JuliaShaders/julia_vs.vert"), new AssetUri("asset:///JuliaShaders/julia_ssao.frag"), null, macros));

            var ssaoPass = new CreateRenderPassCommand("ssaopass", Renderer.MainCameraName, "ssao_result_texture", ++order, width, height, SSAO_Format, DepthStencilFormats.None, false);
            ssaoPass.SetOverrideShader("ssao_shader");
            ssaoPass.AddInputTexture("input_depth_texture", depth_texture_name);
            ssaoPass.AddInputTexture("input_normal_texture", normal_texture_name);
            processor.AddCommand(ssaoPass);

            processor.AddCommand(new Create2DTextureCommand("ssao_blur_temp", width, height, SSAO_Format));

            processor.AddCommand(new CreateComputePassCommand("blurSSAOH", ++order, "gaussianH_ssao", CalculateSSAOBlurThreadgroups(width), CalculateSSAOBlurThreadgroups(height), 1));
            processor.AddCommand(new AddRenderPassInputTexture("blurSSAOH", "sourceImage", "ssao_result_texture", TextureAccessMode.Sampler));
            processor.AddCommand(new AddRenderPassInputTexture("blurSSAOH", "destImage", "ssao_blur_temp", TextureAccessMode.ReadWrite));

            processor.AddCommand(new CreateComputePassCommand("blurSSAOV", ++order, "gaussianV_ssao", CalculateSSAOBlurThreadgroups(width), CalculateSSAOBlurThreadgroups(height), 1));
            processor.AddCommand(new AddRenderPassInputTexture("blurSSAOV", "sourceImage", "ssao_blur_temp", TextureAccessMode.Sampler));
            processor.AddCommand(new AddRenderPassInputTexture("blurSSAOV", "destImage", "ssao_result_texture", TextureAccessMode.ReadWrite));

            processor.AddCommand(new AddRenderPassInputTexture(Renderer.ScreenRenderPassName, "ssaoTexture", "ssao_result_texture"));
        }

        public static void SetupJuliaScene(Processor processor, int AS_Size, int resolution_sq, Vector3 pos, float zoom, int iterations, float timeCoord, int timeCoordIdx, float xray, bool trilinear, int ssao_samples, float camera_fov)
        {
            int screen_width = 640, screen_height=480;
            AccelerationStructureSize = AS_Size;
            BuildAS = AS_Size > 0;
            
            string JuliaMacroParameters = //
            (BuildAS ? ("#define USE_AS 1\n") :  "") +
            "#define AS_Size " + AccelerationStructureSize + "\n" + //
            "#define BlockSizeX " + blockSizeX + "\n" + //
            "#define BlockSizeY " + blockSizeY + "\n" + //
            "#define BlockSizeZ " + blockSizeZ + "\n"; //

            int resolution = (int)Math.Pow(2, resolution_sq);

            int computeGroupsX = resolution / blockSizeX;
            int computeGroupsY = resolution / blockSizeY;
            int computeGroupsZ = resolution / blockSizeZ;
            int nodeid = 1000;

            processor.AddCommand(new CreateIncludableShaderCommand("julia_common", new AssetUri("asset:///JuliaShaders/julia_common.shader")));
            processor.AddCommand(new CreateIncludableShaderCommand("geometry_tools", new AssetUri("asset:///Shaders/geometry_tools.shader")));
            processor.AddCommand(new CreateIncludableShaderCommand("color_tools", new AssetUri("asset:///Shaders/color_tools.shader")));
            processor.AddCommand(new CreateIncludableShaderCommand("noise", new AssetUri("asset:///Shaders/noise.shader")));

            List<AssetUri> skyboxTexturelist = new List<AssetUri>();
            skyboxTexturelist.Add(new AssetUri("asset:///Assets/Skybox/posx.png"));
            skyboxTexturelist.Add(new AssetUri("asset:///Assets/Skybox/negx.png"));
            skyboxTexturelist.Add(new AssetUri("asset:///Assets/Skybox/posy.png"));
            skyboxTexturelist.Add(new AssetUri("asset:///Assets/Skybox/negy.png"));
            skyboxTexturelist.Add(new AssetUri("asset:///Assets/Skybox/posz.png"));
            skyboxTexturelist.Add(new AssetUri("asset:///Assets/Skybox/negz.png"));
            processor.AddCommand(new LoadCubeTextureCommand("skybox_texture", skyboxTexturelist));

            processor.AddCommand(new CreateBuffer("julia4DResult", (long)resolution * resolution * (resolution / 8)));

            {
                //Compute pass
                processor.AddCommand(new CreateComputeShaderCommand("cs_voxelizer", new AssetUri("asset:///JuliaShaders/julia_voxel.comp"), JuliaMacroParameters));

                var cmd = new CreateComputePassCommand("pass_voxelize", -100, "cs_voxelizer", computeGroupsX, computeGroupsY, computeGroupsZ);
                cmd.SetIsTriggeredManually(true);
                processor.AddCommand(cmd);
                processor.AddCommand(new SetRenderPassBufferCommand("pass_voxelize", "julia4DResult", 2));
                processor.AddCommand(new IncrementRenderPass("pass_voxelize"));
            }

            if(BuildAS) //Acceleration structure
            {
                processor.AddCommand(new CreateBuffer("julia4DAS", (long)AccelerationStructureSize * AccelerationStructureSize * (AccelerationStructureSize / 8)));
                
                int as_computeGroupsX = AccelerationStructureSize / blockSizeX;
                int as_computeGroupsY = AccelerationStructureSize / blockSizeY;
                int as_computeGroupsZ = AccelerationStructureSize / blockSizeZ;

                processor.AddCommand(new CreateComputeShaderCommand("cs_build_acceleration_structure", new AssetUri("asset:///JuliaShaders/julia_as.comp"), JuliaMacroParameters));

                var cmd = new CreateComputePassCommand("pass_build_as", -99, "cs_build_acceleration_structure", as_computeGroupsX, as_computeGroupsY, as_computeGroupsZ);
                cmd.SetIsTriggeredManually(true);
                processor.AddCommand(cmd);
                processor.AddCommand(new SetRenderPassBufferCommand("pass_build_as", "julia4DResult", 2));
                processor.AddCommand(new SetRenderPassBufferCommand("pass_build_as", "julia4DAS", 3));
                processor.AddCommand(new IncrementRenderPass("pass_build_as"));
            }

            {
                processor.AddCommand(new CreateShaderCommand("julia_depth_shader", new AssetUri("asset:///JuliaShaders/julia_vs.vert"), new AssetUri("asset:///JuliaShaders/julia_depth.frag"), null, JuliaMacroParameters));

                var render_targets = new List<RenderTargetDescriptor>();
                
                RenderTargetDescriptor linear_depth_rt;
                linear_depth_rt.name = "tex_julia_depth";
                linear_depth_rt.linearFiltering = true;
                linear_depth_rt.format = TextureFormats.R32_Float;
                linear_depth_rt.repeatMode = TexRepeatMode.ClampToEdge;
                render_targets.Add(linear_depth_rt);
                
                RenderTargetDescriptor normals_rt;
                normals_rt.name = "tex_julia_normals";
                normals_rt.linearFiltering = false;
                normals_rt.format = TextureFormats.R8G8B8_Snorm;
                normals_rt.repeatMode = TexRepeatMode.ClampToEdge;
                render_targets.Add(normals_rt);

                var depthPass = new CreateRenderPassCommand("pass_depth_render", Renderer.MainCameraName, -50, screen_width, screen_height, render_targets, DepthStencilFormats.None);
                depthPass.SetOverrideShader("julia_depth_shader");
                processor.AddCommand(depthPass);
                processor.AddCommand(new SetRenderPassBufferCommand("pass_depth_render", "julia4DResult", 2));

                if(BuildAS)
                {
                    processor.AddCommand(new SetRenderPassBufferCommand("pass_depth_render", "julia4DAS", 3));
                }
            }

            SetupSSAO(processor, "tex_julia_depth", "tex_julia_normals", -40, screen_width, screen_height, JuliaMacroParameters);

            {
                var material = new SetMaterialUniformCommand("fsquad_mat");
                material.AddDiffuseTexture("tex_julia_depth");
                material.AddNormalTexture("tex_julia_normals");
                processor.AddCommand(material);
            }

            //#############
            //Shaders
            //###########

            processor.AddCommand(new CreateShaderCommand("shader_generic", new AssetUri("asset:///JuliaShaders/julia_vs.vert"), new AssetUri("asset:///JuliaShaders/julia_display.frag"), null, JuliaMacroParameters));

            processor.AddCommand(new AddRenderPassInputTexture(Renderer.ScreenRenderPassName, "skybox_background", "skybox_texture"));

            processor.AddCommand(new SetComputeShaderUniformCommand<float>("u_resolution_f", (float)resolution));
            processor.AddCommand(new SetComputeShaderUniformCommand<int>("u_resolution_i", resolution));
            processor.AddCommand(new SetComputeShaderUniformCommand<int>("u_iterations", iterations));
            processor.AddCommand(new SetComputeShaderUniformCommand<float>("u_timeCoord", timeCoord));
            processor.AddCommand(new SetComputeShaderUniformCommand<float>("u_zoom", zoom));
            processor.AddCommand(new SetComputeShaderUniformCommand<Vector3>("u_pan", pos));
            processor.AddCommand(new SetComputeShaderUniformCommand<int>("u_timeCoordIndex", timeCoordIdx));

            processor.AddCommand(new SetUniformCommand<float>("u_resolution_f", (float)resolution));
            processor.AddCommand(new SetUniformCommand<float>("normedVoxelSize", 1.0f / ((float)resolution)));
            processor.AddCommand(new SetUniformCommand<int>("u_resolution_i", resolution));

            processor.AddCommand(new SetUniformCommand<float>("u_xray_percent", xray));
            processor.AddCommand(new SetUniformCommand<int>("u_smoothing", trilinear ? 1 : 0));
            processor.AddCommand(new SetUniformCommand<float>("u_camera_fov", camera_fov));

            processor.AddCommand(new CreateFSQuadMesh("FSQuad", "fsquad_mat", true));
            processor.AddCommand(new CreateMeshNodeCommand("FSQuad", "shader_generic", ++nodeid, 0));



        }
    }
}
