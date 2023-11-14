using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using Unity.Mathematics;
using UnityEditor;


public class PBDCloth : MonoBehaviour
{
    public int numberParticles = 50;
    public int solverIterations = 10;
    public ComputeShader pbdShader;

    public bool useSpheres;

    private List<GameObject> spheres = new List<GameObject>();
    public Material clothMaterial;

    [Header("Movement")]
    public bool useSin = true;
    public float sinSpeed;
    public float sinRadius;

    [Header("Shader uniforms")]
    //public float deltaTime = 1f/30f; //fixed deltaTime
    public Vector4 externalForce; //force applied to each particle
    public float constraintDistance = 1f; //distance enforces between each particle\

    public float maxBending;
    public float normalCompliance;
    public Vector3 normal;

    public float drag;
    public float mouseInfluenceRadius;
    public bool mouseShouldCut;

    [Header("WIP")]
    public int numRopes; //x
    public int particlesPerRope; //y

    [Header("Backlog")]
    public float maxStretching;
    public float tearThreshold;

    //[Header("Sphere SDF")]
    //public GameObject sphere;

    //public Mesh sourceMesh;
    //public Material material;
    
    private ComputeBuffer vertexBufferA;
    private ComputeBuffer vertexBufferB;
    private ComputeBuffer edgeBuffer;

    private bool bufferAWrite;

    //32 bytes, 8 bytes alignment
    private struct VerletVertex {
        public float3 position; //12 bytes
        public float isAnchor; //4 bytes
        public float3 oldPosition; //12 bytes
        public float isConnected; //4 bytes
        public float rootIdx; //4
        public float isConnectedLeft; //4
        public float3 velocity; //12
        public float padding0; //4
    }

    private VerletVertex[] vertexData;
    private Vector3 worldMousePos;

    public Mesh mesh;

    // Start is called before the first frame update
    void Start()
    {
        Application.targetFrameRate = 60;

        mesh = new Mesh();
        var meshFilter = gameObject.AddComponent<MeshFilter>();
        meshFilter.mesh = mesh;
        var meshRenderer = gameObject.AddComponent<MeshRenderer>();
        meshRenderer.material = clothMaterial;

        Vector3[] verts = new Vector3[numberParticles];
        Vector2[] UVs = new Vector2[numberParticles];
        int numIndices = (numRopes - 1) * (particlesPerRope - 1) * 6;
        int[] triIndices = new int[numIndices];

        vertexBufferA = new ComputeBuffer(numberParticles, Marshal.SizeOf(typeof(VerletVertex)));
        vertexBufferB = new ComputeBuffer(numberParticles, Marshal.SizeOf(typeof(VerletVertex)));

        //initialize vertex data
        vertexData = new VerletVertex[numberParticles];
        for (int i = 0; i < numberParticles; i++){
            float currRope = math.floor(i / particlesPerRope) * constraintDistance;
            float currRopePos = (i % particlesPerRope) * constraintDistance;

            var newVertex = new VerletVertex();
            newVertex.position = new Vector3(currRope, -currRopePos, 0f);
            newVertex.oldPosition = newVertex.position;

            newVertex.isAnchor = (i % particlesPerRope == 0) ? 1f : 0f;
            newVertex.isConnected = (i % particlesPerRope == 0) ? 0f : 1f;
            newVertex.isConnectedLeft = (i <= particlesPerRope) ? 0f : 1f;
            newVertex.rootIdx = math.floor(i / particlesPerRope) * particlesPerRope;

            vertexData[i] = newVertex;

            verts[i] = newVertex.position;
            UVs[i] = Vector2.zero; //todo actual uv mapping

            if (useSpheres)
            {
                spheres.Add(GameObject.CreatePrimitive(PrimitiveType.Sphere));
                spheres[i].transform.localScale = new Vector3(0.2f, 0.2f, 0.2f);
                spheres[i].transform.position = newVertex.position;
                MeshRenderer mr = spheres[i].GetComponent<MeshRenderer>();
                if (i % particlesPerRope != particlesPerRope - 1)
                {
                    if (i % 2 == 0) mr.material.SetColor("_Color", Color.yellow);
                    else mr.material.SetColor("_Color", Color.green);
                }
                else mr.material.SetColor("_Color", Color.red);
                if (i % particlesPerRope == 0)
                {
                    mr.material.SetColor("_Color", Color.blue);
                }
            }

        }
        vertexBufferA.SetData(vertexData);
        vertexBufferB.SetData(vertexData);

        for (int i = 0, j = 0; 
            i < numIndices; 
            i+=6, j = ((j + 1) % particlesPerRope) == (particlesPerRope - 1) ? (j + 2) : (j + 1)
            )
        {
            triIndices[i] = j;
            triIndices[i + 1] = j + particlesPerRope + 1;
            triIndices[i + 2] = j + 1;

            triIndices[i + 3] = j + particlesPerRope;
            triIndices[i + 4] = j + particlesPerRope + 1;
            triIndices[i + 5] = j;
        }

        mesh.vertices = verts;
        mesh.uv = UVs;
        mesh.triangles = triIndices;

        bufferAWrite = true;

        UpdateUniforms();
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        PBDUpdate();
    }

    void XPBDUpdate(){
        UpdateUniforms();
        pbdShader.SetFloat("_DeltaTime", Time.fixedDeltaTime / solverIterations);

        for (int i = 0; i < solverIterations; i++){
            //verlet integration
            bufferAWrite = !bufferAWrite;
            pbdShader.SetBuffer(0, "_VerletVertexWriteBuffer", bufferAWrite ? vertexBufferA : vertexBufferB);
            pbdShader.SetBuffer(0, "_VerletVertexReadBuffer", bufferAWrite ? vertexBufferB : vertexBufferA);
            pbdShader.Dispatch(0, Mathf.CeilToInt(vertexBufferA.count / 64f), 1, 1); //split work across 64 threads

            bufferAWrite = !bufferAWrite;
            pbdShader.SetBuffer(1, "_VerletVertexWriteBuffer", bufferAWrite ? vertexBufferA : vertexBufferB);
            pbdShader.SetBuffer(1, "_VerletVertexReadBuffer", bufferAWrite ? vertexBufferB : vertexBufferA);
            pbdShader.Dispatch(1, Mathf.CeilToInt(vertexBufferA.count / 64f), 1, 1);

            bufferAWrite = !bufferAWrite;
            pbdShader.SetBuffer(2, "_VerletVertexWriteBuffer", bufferAWrite ? vertexBufferA : vertexBufferB);
            pbdShader.SetBuffer(2, "_VerletVertexReadBuffer", bufferAWrite ? vertexBufferB : vertexBufferA);
            pbdShader.Dispatch(2, Mathf.CeilToInt(vertexBufferA.count / 64f), 1, 1);
        }

        if (bufferAWrite) 
            vertexBufferA.GetData(vertexData);
        else
            vertexBufferB.GetData(vertexData);
    }

    void PBDUpdate(){
        UpdateUniforms();

        //verlet integration
        bufferAWrite = !bufferAWrite;
        pbdShader.SetBuffer(0, "_VerletVertexWriteBuffer", bufferAWrite ? vertexBufferA : vertexBufferB);
        pbdShader.SetBuffer(0, "_VerletVertexReadBuffer", bufferAWrite ? vertexBufferB : vertexBufferA);
        pbdShader.Dispatch(0, Mathf.CeilToInt(vertexBufferA.count / 64f), 1, 1); //split work across 64 threads

        //solve distance constrains
        //TODO: swap buffers A and B each iteration
        for (int i = 0; i < solverIterations; i++){
            bufferAWrite = !bufferAWrite;
            pbdShader.SetBuffer(1, "_VerletVertexWriteBuffer", bufferAWrite ? vertexBufferA : vertexBufferB);
            pbdShader.SetBuffer(1, "_VerletVertexReadBuffer", bufferAWrite ? vertexBufferB : vertexBufferA);
            pbdShader.Dispatch(1, Mathf.CeilToInt(vertexBufferA.count / 64f), 1, 1);
        }

        bufferAWrite = !bufferAWrite;
        pbdShader.SetBuffer(2, "_VerletVertexWriteBuffer", bufferAWrite ? vertexBufferA : vertexBufferB);
        pbdShader.SetBuffer(2, "_VerletVertexReadBuffer", bufferAWrite ? vertexBufferB : vertexBufferA);
        pbdShader.Dispatch(2, Mathf.CeilToInt(vertexBufferA.count / 64f), 1, 1);

        //debug

        if (bufferAWrite) 
            vertexBufferA.GetData(vertexData);
        else
            vertexBufferB.GetData(vertexData);

        if (useSpheres)
        {
            for (int i = 0; i < numberParticles; i++)
            {
                spheres[i].transform.position = vertexData[i].position;
            }
        }
        else
        {
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < numberParticles; i++)
            {
                vertices[i] = vertexData[i].position;
            }
            mesh.vertices = vertices;
        }
    }

    private void UpdateUniforms() {
        pbdShader.SetVector("_OldMousePosition", worldMousePos);

        Vector4 newAnchorPos = Vector4.zero;
        worldMousePos = Camera.main.ScreenToWorldPoint(
                new Vector3(Input.mousePosition.x, Input.mousePosition.y, -Camera.main.transform.position.z));
        //move the anchor point on a sin wave
        if (useSin){
            newAnchorPos = new Vector4(Mathf.Sin(Time.realtimeSinceStartup * sinSpeed) * sinRadius, 5.0f, 0.0f);
        }
        else{ //use mouse pos
            newAnchorPos = new Vector4(worldMousePos.x, worldMousePos.y, 0f, 0f);
        }

        pbdShader.SetFloat("_NumRopes", numRopes);
        pbdShader.SetFloat("_ParticlesPerRope", particlesPerRope);

        pbdShader.SetVector("_AnchorPosition", newAnchorPos);
        pbdShader.SetFloat("_DeltaTime", Time.fixedDeltaTime);
        pbdShader.SetFloat("_ConstraintDistance", constraintDistance);
        pbdShader.SetVector("_ExternalForce", externalForce);
        pbdShader.SetFloat("_MaxBending", maxBending);
        pbdShader.SetFloat("_NormalCompliance", math.saturate(normalCompliance));
        pbdShader.SetVector("_Normal", normal.normalized);
        pbdShader.SetFloat("_Drag", drag);
        pbdShader.SetFloat("_MaxParticles", numberParticles);

        pbdShader.SetVector("_MousePosition", worldMousePos);
        pbdShader.SetBool("_MouseIsDown", Input.GetMouseButton(0));
        pbdShader.SetFloat("_MouseInfluenceRadius", mouseInfluenceRadius);
        pbdShader.SetBool("_MouseShouldCut", mouseShouldCut);

        pbdShader.SetFloat("_MaxStretching", maxStretching);
        pbdShader.SetFloat("_TearThreshold", tearThreshold);

        //pbdShader.SetVector("_SphereCenter", sphere.transform.position);
        //pbdShader.SetFloat("_SphereRadius", sphere.transform.lossyScale.x);
        
    }
}
