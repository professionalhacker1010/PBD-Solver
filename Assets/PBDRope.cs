using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using UnityEngine;
using Unity.Mathematics;
using UnityEditor;

public class PBDRope : MonoBehaviour
{
    public int numberParticles = 50;
    public int solverIterations = 10;
    public ComputeShader pbdShader;

    [Header("Movement")]
    public bool useSin = true;
    public float sinSpeed;
    public float sinRadius;

    [Header("Shader uniforms")]
    //public float deltaTime = 1f/30f; //fixed deltaTime
    public Vector4 externalForce; //force applied to each particle
    public float constraintDistance = 1f; //distance enforces between each particle\
    public float maxBending;
    public float drag;
    public float mouseInfluenceRadius;
    public bool mouseShouldCut;

    [Header("WIP")]
    public int numRopes; //x
    public int particlesPerRope; //y

    [Header("Backlog")]
    public float maxStretching;
    public float tearThreshold;
    public Transform anchorPosition2;

    //[Header("Sphere SDF")]
    //public GameObject sphere;

    //public Mesh sourceMesh;
    //public Material material;
    
    private ComputeBuffer vertexBufferA;
    //private ComputeBuffer vertexBufferB;
    private ComputeBuffer edgeBuffer;

   // private bool bufferSwap;

    //32 bytes, 8 bytes alignment
    private struct VerletVertex {
        public float3 position; //12 bytes
        public float isAnchor; //4 bytes
        public float3 oldPosition; //12 bytes
        public float isConnected; //4 bytes
        public float rootIdx; //4
        public float padding0; //4
    }

    private struct VerletEdge {
        public int v1;
        public int v2;
        public float padding0;
        public float padding1;
    }

    private VerletVertex[] vertexData;
    private Vector3 worldMousePos;

    // Start is called before the first frame update
    void Start()
    {
        Application.targetFrameRate = 60;
        
        //bufferSwap = false;

        vertexBufferA = new ComputeBuffer(numberParticles, Marshal.SizeOf(typeof(VerletVertex)));

        //initialize vertex data
        vertexData = new VerletVertex[numberParticles];
        for (int i = 0; i < numberParticles; i++){
            var newVertex = new VerletVertex();
            newVertex.position = new Vector3(0f, 0f, 0f);
            newVertex.oldPosition = newVertex.position;

            //newVertex.isAnchor = (i == 0) ? 1f : 0f;
            //newVertex.isConnected = (i == 0) ? 0f : 1f;
            //newVertex.rootIdx = 0f;
            //int particlesPerRope = numberParticles / numRopes;
            newVertex.isAnchor = (i % particlesPerRope == 0) ? 1f : 0f;
            newVertex.isConnected = (i % particlesPerRope == 0) ? 0f : 1f;
            newVertex.rootIdx = math.floor(i / particlesPerRope) * particlesPerRope;

            vertexData[i] = newVertex;
        }
        vertexBufferA.SetData(vertexData);

        UpdateUniforms();
    }

    // Update is called once per frame
    void FixedUpdate()
    {
        UpdateUniforms();

        //verlet integration
        pbdShader.SetBuffer(0, "_VerletVertexWriteBuffer", vertexBufferA);
        pbdShader.Dispatch(0, Mathf.CeilToInt(vertexBufferA.count / 64f), 1, 1); //split work across 64 threads

        //solve distance constrains
        //TODO: swap buffers A and B each iteration
        pbdShader.SetBuffer(1, "_VerletVertexWriteBuffer", vertexBufferA);
        for (int i = 0; i < solverIterations; i++){
            pbdShader.Dispatch(1, Mathf.CeilToInt(vertexBufferA.count / 64f), 1, 1);
        }

        vertexBufferA.GetData(vertexData);

        //Debug.Log(vertexData[1].position);
        //Debug.Log(worldMousePos + " mouse");
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

        pbdShader.SetVector("_AnchorPosition", newAnchorPos);
        pbdShader.SetVector("_AnchorPosition2", anchorPosition2.position);
        pbdShader.SetFloat("_DeltaTime", Time.fixedDeltaTime);
        pbdShader.SetFloat("_ConstraintDistance", constraintDistance);
        pbdShader.SetVector("_ExternalForce", externalForce);
        pbdShader.SetFloat("_MaxBending", math.saturate(maxBending));
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

    void OnDrawGizmos(){
        if (EditorApplication.isPlaying){
            //draw spheres
            
            for (int i = 0; i < numberParticles; i++)
            {
                if (i % particlesPerRope != particlesPerRope - 1) {
                   if (i % 2 == 0) Gizmos.color = Color.yellow;
                   else Gizmos.color = Color.green;
                }
                else Gizmos.color = Color.red;
                Gizmos.DrawSphere(vertexData[i].position, .1f);
            }
        }

    }
}
