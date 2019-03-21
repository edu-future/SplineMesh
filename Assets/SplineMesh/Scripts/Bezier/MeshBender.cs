﻿using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.Linq;
using static SplineMesh.ExtrusionSegment;

namespace SplineMesh {
    /// <summary>
    /// A component that create a deformed mesh from a given one, according to a cubic Bézier curve and other parameters.
    /// The mesh will always be bended along the X axis. Extreme X coordinates of source mesh verticies will be used as a bounding to the deformed mesh.
    /// The resulting mesh is stored in a MeshFilter component and automaticaly updated each time the cubic Bézier curve control points are changed.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter))]
    [ExecuteInEditMode]
    public class MeshBender : MonoBehaviour {
        private bool isDirty = false;
        private Mesh result;
        private bool useSpline;
        private Spline spline;
        private float startDistance, endDistance;

        private SourceMesh source;
        /// <summary>
        /// The source mesh to bend.
        /// </summary>
        public SourceMesh Source {
            get { return source; }
            set {
                if (value == source) return;
                isDirty = true;
                source = value;

                var m = source.Mesh;
                result.hideFlags = m.hideFlags;
                result.indexFormat = m.indexFormat;
                result.vertices = m.vertices.ToArray();

                result.uv = m.uv.ToArray();
                result.uv2 = m.uv2.ToArray();
                result.uv3 = m.uv3.ToArray();
                result.uv4 = m.uv4.ToArray();
                result.uv5 = m.uv5.ToArray();
                result.uv6 = m.uv6.ToArray();
                result.uv7 = m.uv7.ToArray();
                result.uv8 = m.uv8.ToArray();
                result.tangents = m.tangents.ToArray();

                result.triangles = source.Triangles;
            }
        }
        
        private Vector3 translation;
        /// <summary>
        /// The offset to apply to the source mesh before bending it.
        /// </summary>
        public Vector3 Translation {
            get { return translation; }
            set {
                if (value == translation) return;
                isDirty = true;
                translation = value;
            }
        }

        private Quaternion rotation;
        /// <summary>
        /// The rotation to apply to the source mesh before bending it.
        /// Because source mesh will always be bended along the X axis but may be oriented differently.
        /// </summary>
        public Quaternion Rotation {
            get { return rotation; }
            set {
                if (value == rotation) return;
                isDirty = true;
                rotation = value;
            }
        }

        private Vector3 scale = Vector3.one;
        /// <summary>
        /// The scale to apply to the source mesh before bending it.
        /// Scale on X axis is internaly limited to -1;1 to restrain the mesh inside the curve bounds.
        /// </summary>
        public Vector3 Scale {
            get { return scale; }
            set {
                if (value == scale) return;
                isDirty = true;
                scale = value;
            }
        }

        private FillingMode mode = FillingMode.Once;
        /// <summary>
        /// The scale to apply to the source mesh before bending it.
        /// Scale on X axis is internaly limited to -1;1 to restrain the mesh inside the curve bounds.
        /// </summary>
        public FillingMode Mode {
            get { return mode; }
            set {
                if (value == mode) return;
                isDirty = true;
                mode = value;
            }
        }

        private CubicBezierCurve curve = null;

        public void SetInterval(CubicBezierCurve curve) {
            if (this.curve == curve) return;
            if (curve == null) throw new ArgumentNullException("curve");
            if (this.curve != null) {
                this.curve.Changed.RemoveListener(Compute);
            }
            this.curve = curve;
            curve.Changed.AddListener(Compute);
            useSpline = false;
            isDirty = true;
        }

        public void SetInterval(Spline spline, float startDistance, float endDistance = 0) {
            if (spline == null) throw new ArgumentNullException("spline");
            if (startDistance <= 0 || startDistance >= spline.Length) {
                throw new ArgumentOutOfRangeException("start distance must be greater than 0 and lesser than spline length (was " + startDistance + ")");
            }
            this.spline = spline;
            this.startDistance = startDistance;
            this.endDistance = endDistance;
            useSpline = true;
            isDirty = true;
        }


        private void OnEnable() {
            if(GetComponent<MeshFilter>().sharedMesh != null) {
                result = GetComponent<MeshFilter>().sharedMesh;
            } else {
                GetComponent<MeshFilter>().sharedMesh = result = new Mesh();
                result.name = "Generated by " + GetType().Name;
            }
        }

        /// <summary>
        /// Bend the mesh only if a property has changed since the last compute.
        /// </summary>
        public void ComputeIfNeeded() {
            if (!isDirty) return;
            Compute();
        }

        /// <summary>
        /// Bend the mesh. This method may take time and should not be called more than necessary.
        /// Consider using <see cref="ComputeIfNeeded"/> for faster result.
        /// </summary>
        public void Compute() {
            isDirty = false;

            // we manage a cache because in most situations, the mesh will contain several vertices located at the same curve distance.
            Dictionary<float, CurveSample> sampleCache = new Dictionary<float, CurveSample>();

            List<Vertex> bentVertices = new List<Vertex>(vertices.Count);
            // for each mesh vertex, we found its projection on the curve
            foreach (Vertex vert in transformedVertices) {
                Vertex bent = new Vertex() {
                    v = vert.v,
                    n = vert.n
                };
                float distanceRate = length == 0? 0 : Math.Abs(bent.v.x - minX) / length;
                CurveSample sample;
                if(!sampleCache.TryGetValue(distanceRate, out sample)){
                    if(!useSpline) {
                        sample = curve.GetSampleAtDistance(curve.Length * distanceRate);
                    } else {
                        float distOnSpline = startDistance + length * distanceRate;
                        while (distOnSpline > spline.Length) {
                            distOnSpline -= spline.Length;
                        }
                        sample = spline.GetSampleAtDistance(distOnSpline);
                    }
                    sampleCache[distanceRate] = sample;
                }

                // application of scale
                bent.v = Vector3.Scale(bent.v, new Vector3(0, sample.scale.y, sample.scale.x));

                // application of roll
                bent.v = Quaternion.AngleAxis(sample.roll, Vector3.right) * bent.v;
                bent.n = Quaternion.AngleAxis(sample.roll, Vector3.right) * bent.n;

                // reset X value
                bent.v.x = 0;

                // application of the rotation + location
                Quaternion q = sample.Rotation * Quaternion.Euler(0, -90, 0);
                bent.v = q * bent.v + sample.location;
                bent.n = q * bent.n;
                bentVertices.Add(bent);
            }

            result.vertices = bentVertices.Select(b => b.v).ToArray();
            result.normals = bentVertices.Select(b => b.n).ToArray();
            result.RecalculateBounds();
        }

        private void OnDestroy() {
            if(curve != null) {
                curve.Changed.RemoveListener(Compute);
            }
        }

        public enum FillingMode {
            Once,
            Repeat,
            StretchToInterval
        }
    }
}