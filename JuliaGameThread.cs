using Engine.AssetManagement;
using Engine.BackEnd;
using Engine.FrontEnd;
using Engine.Utils;
using Engine.Utils.Logging;
using ImGuiNET;
using OpenTK;
using OpenTK.Input;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Julia4D
{
    public class JuliaGameThread
    {
        private int screen_width=1280, screen_height=720;
        private float screen_res_percentage = 1.0f;
        private float xray = 0.5f;
        private int Julia_AS_Size = 64;
        private float timecoord = 0.0f;
        private int iterations = 100;
        private int resolution_sq = 9;
        private int ssao_samples = 48;
        private float ssao_radius = 0.02f;
        private float ssao_strength = 0.75f;
        private float camera_fov = 0.9f;
        bool ssao_blur = true;
        private Vector3 pan = new Vector3();
        private float zoom = 1.0f;
        private bool trilinear = false;

        private Vector2 light_dir = new Vector2(0, 70);

        private static readonly object syncRoot = new object();
        private static readonly object sync_imgui = new object();
        private bool exit = false;

        public void OnClosing()
        {
            lock (syncRoot)
            {
                exit = true;
            }
        }

        private Vector3 getLightAngle()
        {
            float deg_2_rad = (float)Math.PI / 180.0f;
            Vector4 angle = new Vector4(0,1,0,0);
            angle = angle * Matrix4.CreateRotationX(light_dir.Y * deg_2_rad);
            angle = angle * Matrix4.CreateRotationZ(light_dir.X * deg_2_rad);
            return angle.Xyz.Normalized();
        }

        CameraObj camera = new CameraObj(null, Renderer.MainCameraName);

        private double deltaTime = 0.0f, time = 0.0;
        Processor processor;

        bool show_debug_screen = false;
        private bool is_right_mouse_down = false;
        private string selected_4d_axes = "XYZ";

        struct InputParam
        {
            public string name;
            public float value;
        };
        private Dictionary<Keys, InputParam> key_mapping = new Dictionary<Keys, InputParam>();
        private Dictionary<string, Object> app_config;
        private void DrawImGui()
        {
            lock (sync_imgui)
            {
                if (!ImGui.Begin("Julia 4D"))
                {
                    ImGui.End();
                    return; //early out if collapsed
                }

                var conf = EngineConf.GetInstance();

                int actual_resolution = (int)Math.Pow(2, resolution_sq);
                int x_ray_resolution = Julia_AS_Size > 0 ? Julia_AS_Size : actual_resolution;

                ImGui.BeginTabBar("MainTabBar");
                if (ImGui.BeginTabItem("Info"))
                {
                    ImGui.Text("The Mandelbrot set is a 2D slice of a 4D fractal geometry called Julia set.");
                    ImGui.Text("The 4D julia set is defined as the points where the 'z(n) = z(n-1) + c'");
                    ImGui.Text("series does not diverge.");
                    ImGui.Text("Both 'n' and 'c' are complex numbers, resulting in the 4 dimensions.");
                    ImGui.Text("");
                    ImGui.Text("This app renders 3 dimensions of the fractal out of the 4.");
                    ImGui.Text("The 4th dimension can be scaled using a slider on the 'View' tab.");
                    ImGui.Text("");
                    ImGui.Text("To view the whole fractal, move the 'X-Ray' slider to 1.0!");
                    ImGui.Text("");
                    ImGui.Text("You can move around using W,A,S,D, and view with the Right mouse button.");
                    ImGui.Text("");
                    ImGui.Text("If you have a capable computer, you can increase");
                    ImGui.Text("the fractal resolution in the Rendering tab.");

                    ImGui.EndTabItem();
                }
                if (ImGui.BeginTabItem("View"))
                {
                    ImGui.Separator();
                    ImGui.Text("Camera");

                    ImGui.Text("Camera position:  " + camera.Position.ToString());
                    ImGui.Text("Camera direction: (" + camera.GetDirection().X.ToString("N3") + ", " + camera.GetDirection().Y.ToString("N3") + ", " + camera.GetDirection().Z.ToString("N3") + ")");
                    if(ImGui.SliderFloat("FOV", ref camera_fov, 0.4f, 1.5f))
                    {
                        processor.AddCommand(new SetUniformCommand<float>("u_camera_fov", camera_fov));
                    }

                    ImGui.Separator();
                    ImGui.Text("Fractal view");

                    if (ImGui.BeginCombo("Select 4D->3D axes", selected_4d_axes))
                    {
                        if (ImGui.Selectable("XYZ"))
                        {
                            selected_4d_axes = "XYZ";
                            processor.AddCommand(new SetComputeShaderUniformCommand<int>("u_timeCoordIndex", 3));
                            timecoord = 0;
                            processor.AddCommand(new SetComputeShaderUniformCommand<float>("u_timeCoord", timecoord));
                            Julia4DScenecs.RecalculateFractal(processor);
                        }

                        if (ImGui.Selectable("XYW"))
                        {
                            selected_4d_axes = "XYW";
                            processor.AddCommand(new SetComputeShaderUniformCommand<int>("u_timeCoordIndex", 2));
                            timecoord = 0;
                            processor.AddCommand(new SetComputeShaderUniformCommand<float>("u_timeCoord", timecoord));
                            Julia4DScenecs.RecalculateFractal(processor);
                        }

                        if (ImGui.Selectable("XZW"))
                        {
                            selected_4d_axes = "XZW";
                            processor.AddCommand(new SetComputeShaderUniformCommand<int>("u_timeCoordIndex", 1));
                            timecoord = 0;
                            processor.AddCommand(new SetComputeShaderUniformCommand<float>("u_timeCoord", timecoord));
                            Julia4DScenecs.RecalculateFractal(processor);
                        }

                        if (ImGui.Selectable("YZW"))
                        {
                            selected_4d_axes = "YZW";
                            processor.AddCommand(new SetComputeShaderUniformCommand<int>("u_timeCoordIndex", 0));
                            timecoord = 0;
                            processor.AddCommand(new SetComputeShaderUniformCommand<float>("u_timeCoord", timecoord));
                            Julia4DScenecs.RecalculateFractal(processor);
                        }

                        ImGui.EndCombo();
                    }


                    if (ImGui.SliderFloat("4th dimension control", ref timecoord, -2.0f, 2.0f))
                    {
                        processor.AddCommand(new SetComputeShaderUniformCommand<float>("u_timeCoord", timecoord));
                        Julia4DScenecs.RecalculateFractal(processor);
                    }

                    System.Numerics.Vector3 tmp = new System.Numerics.Vector3();
                    tmp.X = pan.X;
                    tmp.Y = pan.Y;
                    tmp.Z = pan.Z;
                    if(ImGui.SliderFloat3("Pan", ref tmp, -2.0f, 2.0f))
                    {
                        pan.X = tmp.X;
                        pan.Y = tmp.Y;
                        pan.Z = tmp.Z;
                        processor.AddCommand(new SetComputeShaderUniformCommand<Vector3>("u_pan", pan));
                        Julia4DScenecs.RecalculateFractal(processor);
                    }

                    if(ImGui.SliderFloat("Zoom", ref zoom, 0.001f, 2.0f))
                    {
                        float actual_zoom = (float)Math.Pow(2.0f - zoom, 3);
                        processor.AddCommand(new SetComputeShaderUniformCommand<float>("u_zoom", zoom));
                        Julia4DScenecs.RecalculateFractal(processor);
                    }

                    if(ImGui.SliderFloat("XRay", ref xray, 0.0f, 1.0f))
                    {
                        int xray_discrete = (int)(xray * (float)x_ray_resolution);
                        float actual_xray = (float)xray_discrete / (float)x_ray_resolution;
                        processor.AddCommand(new SetUniformCommand<float>("u_xray_percent", actual_xray));
                    }

                    ImGui.Separator();
                    ImGui.Text("Lighting");
                    if(ImGui.SliderFloat("Light Horizontal angle", ref light_dir.X, -180, 180))
                    {
                        processor.AddCommand(new SetUniformCommand<Vector3>("u_lightDir", getLightAngle()));
                    }

                    if(ImGui.SliderFloat("Light Latitude angle", ref light_dir.Y, -90, 90))
                    {
                        processor.AddCommand(new SetUniformCommand<Vector3>("u_lightDir", getLightAngle()));
                    }

                    {
                        var angle = getLightAngle();
                        ImGui.Text("Light direction: (" + angle.X.ToString("N3") + ", " + angle.Y.ToString("N3") + ", " + angle.Z.ToString("N3") + ")");
                    }

                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Rendering"))
                {
                    ImGui.Text("Fractal rendering");
                    if(ImGui.SliderInt("Fractal voxel resolution", ref resolution_sq, 5, 11))
                    {
                        Julia4DScenecs.UpdateResolution(processor, resolution_sq);
                        actual_resolution = (int)Math.Pow(2, resolution_sq);
                        int xray_discrete = (int)(xray * (float)x_ray_resolution);
                        float actual_xray = (float)xray_discrete / (float)x_ray_resolution;
                        processor.AddCommand(new SetUniformCommand<float>("u_xray_percent", actual_xray));
                    }
                    ImGui.Text("Voxel resolution: " + actual_resolution + "x" + actual_resolution + "x" + actual_resolution);
                    ImGui.Text("Acceleration structure: " + ((Julia_AS_Size > 0) ? (String.Format("{0}x{0}x{0}", Julia_AS_Size)) : "Disabled"));

                    if(ImGui.SliderInt("Fractal iterations", ref iterations, 4, 1000))
                    {
                        processor.AddCommand(new SetComputeShaderUniformCommand<int>("u_iterations", iterations));
                        Julia4DScenecs.RecalculateFractal(processor);
                    }

                    /*if(ImGui.Checkbox("Smoothing", ref trilinear))
                    {
                        processor.AddCommand(new SetUniformCommand<int>("u_smoothing", trilinear ? 1 : 0));
                    }*/

                    ImGui.Separator();

                    ImGui.Text("Screen");

                    if(ImGui.BeginCombo("Screen resolution scale", ""+((int)(screen_res_percentage * 100.0f)) + "%"))
                    {
                        if(ImGui.Selectable("100%"))
                        {
                            screen_res_percentage = 1.0f;
                            Julia4DScenecs.ResizeFramebufer(processor, screen_width, screen_height, screen_res_percentage);
                        }
                        if(ImGui.Selectable("75%"))
                        {
                            screen_res_percentage = 0.75f;
                            Julia4DScenecs.ResizeFramebufer(processor, screen_width, screen_height, screen_res_percentage);
                        }
                        if(ImGui.Selectable("50%"))
                        {
                            screen_res_percentage = 0.5f;
                            Julia4DScenecs.ResizeFramebufer(processor, screen_width, screen_height, screen_res_percentage);
                        }
                        ImGui.EndCombo();
                    }

                    ImGui.Text("Rendering resolution: " + (int)(screen_width * screen_res_percentage) + "x" + (int)(screen_height * screen_res_percentage));
                    ImGui.Separator();

                    ImGui.Text("Ambient shadows (SSAO)");

                    if(ImGui.SliderInt("SSAO Sample count", ref ssao_samples, 0, 256))
                    {
                        processor.AddCommand(new SetUniformCommand<int>("u_ssao_samples", ssao_samples));
                        
                        processor.AddCommand(new SetRenderPassActiveCommand("blurSSAOH", ssao_blur && ssao_samples > 0));
                        processor.AddCommand(new SetRenderPassActiveCommand("blurSSAOV", ssao_blur && ssao_samples > 0));
                    }
                    if(ssao_samples > 0)
                    {
                        if(ImGui.SliderFloat("SSAO Radius", ref ssao_radius, 0.001f, 0.4f))
                        {
                            processor.AddCommand(new SetUniformCommand<float>("u_ssao_radius", ssao_radius));
                        }
                        if(ImGui.SliderFloat("SSAO Strength", ref ssao_strength, 0.1f, 1.0f))
                        {
                            processor.AddCommand(new SetUniformCommand<float>("u_ssao_strength", ssao_strength));
                        }
                        if(ImGui.Checkbox("SSAO Blur", ref ssao_blur))
                        {
                            processor.AddCommand(new SetRenderPassActiveCommand("blurSSAOH", ssao_blur && ssao_samples > 0));
                            processor.AddCommand(new SetRenderPassActiveCommand("blurSSAOV", ssao_blur && ssao_samples > 0));
                        }
                    }

                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("Controls"))
                {
                    ImGui.Text("Right mouse + Mouse move: Rotate camera");
                    foreach (var item in key_mapping)
                    {
                        ImGui.Text(item.Key.ToString() + " - " + item.Value.name + " -> " + item.Value.value);
                    }
                }

                ImGui.EndTabBar();
                ImGui.End();
            }
        }

        private void SetupKeyMapping()
        {
            object key_mapping_obj = null;
            if (!app_config.TryGetValue("key_mapping", out key_mapping_obj))
            {
                throw new Exception("No 'key_mapping' field in appconfig!");
            }

            JsonElement json_key_mapping = (JsonElement)key_mapping_obj;

            if (json_key_mapping.ValueKind != JsonValueKind.Object)
            {
                throw new Exception("'key_mapping' is not an object!");
            }

            foreach (var item in json_key_mapping.EnumerateObject())
            {
                Keys key = Keys.A;
                InputParam input_param = new InputParam();
                try
                {
                    key = (Keys)Enum.Parse(typeof(Keys), item.Name);
                    if(item.Value.ValueKind == JsonValueKind.String)
                    {
                        input_param.name = item.Value.GetString();
                        input_param.value = 1.0f;
                    }
                    else if(item.Value.ValueKind == JsonValueKind.Object)
                    {
                        input_param.name = item.Value.GetProperty("input").GetString();
                        input_param.value = (float)item.Value.GetProperty("value").GetDouble();
                    }
                }
                catch (System.Exception)
                {
                    Logger.Log(Severity.Warning, "Could not register key binding for: '" + item.Name);
                }

                key_mapping.Add(key, input_param);
            }
        }

        public JuliaGameThread(Processor _renderer, Dictionary<string, Object> app_config)
        {
            this.app_config = app_config;
            this.processor = _renderer;

            SetupKeyMapping();

            {
                object as_size_value = null;
                if(app_config.TryGetValue("AS_Size", out as_size_value))
                {
                    JsonElement json_as_size_value = (JsonElement)as_size_value;
                    json_as_size_value.TryGetInt32(out Julia_AS_Size);
                }
            }

            Julia4DScenecs.SetupJuliaScene(processor, Julia_AS_Size, resolution_sq, pan, zoom, iterations, timecoord, 3, xray, trilinear, ssao_samples, camera_fov);

            processor.AddCommand(new SetUniformCommand<Vector3>("u_lightDir", getLightAngle()));
            processor.AddCommand(new SetUniformCommand<int>("u_ssao_samples", ssao_samples));
            processor.AddCommand(new SetUniformCommand<float>("u_ssao_radius", ssao_radius));
            processor.AddCommand(new SetUniformCommand<float>("u_ssao_strength", ssao_strength));

            processor.AddCommand(new AddImGuiUICommand("Julia_imgui" ,()=>{
                DrawImGui();
            }));


            new Thread(() =>
            {
                Thread.CurrentThread.IsBackground = true;
                int nodeId = 12000;

                camera.Position = new Vector3(-1.725f, -0.8578f, 0.44065f);
                camera.Direction = new Vector3(0.573f, 0.74f, -0.352f);

                while (!exit)
                {
                    lock (syncRoot)
                    {
                        System.Threading.Monitor.Wait(syncRoot);
                        
                        lock(sync_imgui)
                        {
                            if(AbstractInput.Instance.GetInput("imgui") != 0.0f)
                            {
                                show_debug_screen = !show_debug_screen;
                                processor.AddCommand(new SetDebugWindowCommand(show_debug_screen));
                                AbstractInput.Instance.SetInput("imgui", 0.0f);
                            }

                            float speed_multiplier = 0.5f;
                            if(AbstractInput.Instance.GetInput("Sprint") != 0.0f)
                            {
                                speed_multiplier *= AbstractInput.Instance.GetInput("Sprint") * 5.0f;
                            }
                            if(AbstractInput.Instance.GetInput("Walk") != 0.0f)
                            {
                                speed_multiplier *= 0.2f;
                            }


                            var pos = camera.Position;
                            pos += camera.GetDirection() * new Vector3(AbstractInput.Instance.GetInput("Forward") * (float)deltaTime * speed_multiplier);
                            pos += camera.GetRightVector() * new Vector3(AbstractInput.Instance.GetInput("Right") * (float)deltaTime * speed_multiplier);
                            pos += camera.GetUpVector() * new Vector3(AbstractInput.Instance.GetInput("Up") * (float)deltaTime * speed_multiplier);
                            camera.Position = pos;


                            if(!show_debug_screen && is_right_mouse_down)
                            {
                                float mouseXDelta = AbstractInput.Instance.GetInput("MouseXDelta");
                                float mouseYDelta = AbstractInput.Instance.GetInput("MouseYDelta");

                                float yaw = camera.Yaw + mouseXDelta * 0.03f;
                                float pitch = Math.Max( -1.5f, Math.Min(1.5f, camera.Pitch + mouseYDelta * -0.03f));

                                camera.RotateTo(yaw, pitch);
                            }

                            if (AbstractInput.Instance.GetInput("dbg") > 0.0f)
                            {
                                Logger.Log(Severity.Information, "camdir: " + camera.GetDirection());
                                Logger.Log(Severity.Information, "camright: " + camera.GetRightVector());
                                Logger.Log(Severity.Information, "camup: " + camera.GetUpVector());
                            }

                            AbstractInput.Instance.SetInput("MouseXDelta", 0 );
                            AbstractInput.Instance.SetInput("MouseYDelta", 0 );

                            camera.Serialize(processor);

                            if (AbstractInput.Instance.GetInput("Forward3") != 0.0f || AbstractInput.Instance.GetInput("Right3") != 0.0f || AbstractInput.Instance.GetInput("Up3") != 0.0f)
                            {
                                pan += new Vector3(1,0,0) * AbstractInput.Instance.GetInput("Forward3") * zoom * 0.05f; 
                                pan += new Vector3(0,-1,0) * AbstractInput.Instance.GetInput("Right3") * zoom * 0.05f;
                                pan += new Vector3(0, 0, 1) * AbstractInput.Instance.GetInput("Up3") * zoom * 0.05f;
                                processor.AddCommand(new SetComputeShaderUniformCommand<Vector3>("u_pan", pan));
                                Julia4DScenecs.RecalculateFractal(processor);
                                AbstractInput.Instance.SetInput("Forward3", 0.0f);
                                AbstractInput.Instance.SetInput("Right3", 0.0f);
                                AbstractInput.Instance.SetInput("Up3", 0.0f);
                            }

                            if (AbstractInput.Instance.GetInput("MouseWheel") != 0.0f )
                            {
                                float wheel = AbstractInput.Instance.GetInput("MouseWheel");
                                if (wheel > 0)
                                    camera_fov = Math.Min(camera_fov + 0.1f, 1.5f);
                                else
                                    camera_fov = Math.Max(camera_fov - 0.1f, 0.4f);
                                processor.AddCommand(new SetUniformCommand<float>("u_camera_fov", camera_fov));
                                AbstractInput.Instance.SetInput("MouseWheel", 0.0f);
                            }

                            deltaTime = 0;
                        }
                    }
                }
            }).Start();
        }


        public void OnResize(int newWidth, int newHeight)
        {
            lock (syncRoot)
            {
                camera.AspectRatio = (float)newWidth / (float)newHeight;
                Julia4DScenecs.ResizeFramebufer(processor, newWidth, newHeight, screen_res_percentage);
                screen_width = newWidth;
                screen_height = newHeight;
                System.Threading.Monitor.Pulse(syncRoot);
            }
        }

        public void OnMouseWheel(MouseWheelEventArgs e)
        {
            lock (syncRoot)
            {
                AbstractInput.Instance.SetInput("MouseWheel", e.OffsetY);

                System.Threading.Monitor.Pulse(syncRoot);
            }
        }

        public void OnMouseUp(MouseButtonEventArgs e)
        {
            lock (syncRoot)
            {
                if(e.Button == MouseButton.Button2)
                {
                    is_right_mouse_down = false;
                }
                System.Threading.Monitor.Pulse(syncRoot);
            }
        }

        public void OnMouseMove(MouseMoveEventArgs e)
        {
            lock (syncRoot)
            {
                AbstractInput.Instance.SetInput("MouseX", e.X);
                AbstractInput.Instance.SetInput("MouseY", e.Y);
                AbstractInput.Instance.SetInput("MouseXDelta", e.DeltaX + AbstractInput.Instance.GetInput("MouseXDelta") );
                AbstractInput.Instance.SetInput("MouseYDelta", e.DeltaY + AbstractInput.Instance.GetInput("MouseYDelta") ); 

                System.Threading.Monitor.Pulse(syncRoot);
            }
        }

        public void OnMouseDown(MouseButtonEventArgs e)
        {
            lock (syncRoot)
            {
                if(e.Button == MouseButton.Button2)
                {
                    is_right_mouse_down = true;
                }
                System.Threading.Monitor.Pulse(syncRoot);
            }
        }

        public void OnKeyDown(KeyboardKeyEventArgs e)
        {
            lock (syncRoot)
            {
                InputParam input_param;
                if(key_mapping.TryGetValue(e.Key, out input_param))
                {
                    AbstractInput.Instance.SetInput(input_param.name, input_param.value);
                }

                System.Threading.Monitor.Pulse(syncRoot);
            }
        }

        public void OnKeyUp(KeyboardKeyEventArgs e)
        {
            lock (syncRoot)
            {
                InputParam input_param;
                if(key_mapping.TryGetValue(e.Key, out input_param))
                {
                    AbstractInput.Instance.SetInput(input_param.name, 0.0f);
                }

                System.Threading.Monitor.Pulse(syncRoot);
            }
        }

        public void Tick(double t, double dt)
        {
            lock (syncRoot)
            {
                time = t;
                deltaTime += dt;

                System.Threading.Monitor.Pulse(syncRoot);
            }
        }

    }
}
