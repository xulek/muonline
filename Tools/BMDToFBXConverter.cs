using System;
using System.IO;
using System.Numerics;
using System.Collections.Generic;
using System.Threading.Tasks;
using Client.Data.BMD;
using Assimp;
using Assimp.Configs;
using Client.Main.Content;

namespace Tools
{
    public class BMDToFBXConverter
    {
        private readonly AssimpContext _context;

        public BMDToFBXConverter()
        {
            _context = new AssimpContext();
        }

        public async Task<bool> ConvertBMDToFBX(string bmdFilePath, string outputPath)
        {
            try
            {
                var buffer =  await  BMDLoader.Instance.Prepare(bmdFilePath);
                var scene = CreateAssimpScene(buffer);
                
                return _context.ExportFile(scene, outputPath, "fbx", 
                    PostProcessSteps.Triangulate | PostProcessSteps.GenerateNormals);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"转换失败 {bmdFilePath}: {ex.Message}");
                return false;
            }
        }

        private Scene CreateAssimpScene(BMD bmd)
        {
            var scene = new Scene();
            scene.RootNode = new Node("Root");

            // 创建材质
            var materials = CreateMaterials(bmd);
            scene.Materials.AddRange(materials);

            // 创建网格
            foreach (var mesh in bmd.Meshes)
            {
                var assimpMesh = CreateMesh(mesh);
                scene.Meshes.Add(assimpMesh);
                
                var meshNode = new Node($"Mesh_{scene.Meshes.Count - 1}");
                meshNode.MeshIndices.Add(scene.Meshes.Count - 1);
                scene.RootNode.Children.Add(meshNode);
            }

            return scene;
        }

        private List<Material> CreateMaterials(BMD bmd)
        {
            var materials = new List<Material>();
            
            foreach (var mesh in bmd.Meshes)
            {
                var material = new Material();
                material.Name = $"Material_{materials.Count}";
                
                if (!string.IsNullOrEmpty(mesh.TexturePath))
                {
                    material.TextureDiffuse = new TextureSlot(
                        mesh.TexturePath, 
                        TextureType.Diffuse, 
                        0, TextureMapping.FromUV, 0, 0, 
                        TextureOperation.Add, TextureWrapMode.Wrap, TextureWrapMode.Wrap, 0);
                }
                
                materials.Add(material);
            }
            
            return materials;
        }

        private Mesh CreateMesh(BMDTextureMesh bmdMesh)
        {
            var mesh = new Mesh("", PrimitiveType.Triangle);
            
            // 添加顶点位置
            foreach (var vertex in bmdMesh.Vertices)
            {
                mesh.Vertices.Add(new Vector3D(vertex.Position.X, vertex.Position.Y, vertex.Position.Z));
            }
            
            // 添加法线
            foreach (var normal in bmdMesh.Normals)
            {
                mesh.Normals.Add(new Vector3D(normal.Normal.X, normal.Normal.Y, normal.Normal.Z));
            }
            
            // 添加纹理坐标
            foreach (var texCoord in bmdMesh.TexCoords)
            {
                mesh.TextureCoordinateChannels[0].Add(new Vector3D(texCoord.U, texCoord.V, 0));
            }

            // 添加面片 - 每个三角形使用顶点索引
            foreach (var triangle in bmdMesh.Triangles)
            {
                // BMDTriangle包含4个顶点的索引数组，根据Polygon字段确定实际使用的顶点数
                var vertexCount = triangle.Polygon == 3 ? 3 : 4;
                var indices = new int[vertexCount];
                
                for (int i = 0; i < vertexCount; i++)
                {
                    indices[i] = (int)triangle.VertexIndex[i];
                }
                
                mesh.Faces.Add(new Face(indices));
            }

            return mesh;
        }

        public void Dispose()
        {
            _context?.Dispose();
        }
    }
}