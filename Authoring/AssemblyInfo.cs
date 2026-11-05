using System.Reflection;
using Unity.Entities;
using UnityEngine;

[assembly: AssemblyDescription("Serializable animation authoring boundary")]
// Baker.GetComponent<Animator>() tracks its presence/absence through TypeManager.
[assembly: RegisterUnityEngineComponentType(typeof(Animator))]
