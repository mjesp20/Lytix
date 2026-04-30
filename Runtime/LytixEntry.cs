
using System;
using System.Collections.Generic;
using UnityEngine;

namespace LytixInternal
{

    [Serializable]
    public class LytixEntry
    {
        [Serializable]
        public class PlayerPosition
        {
            public float x;
            public float y;
            public float z;

            public Vector3 ToVector3()
            {
                return new Vector3(x, y, z);
            }
        }

        [Serializable]
        public class Entry
        {
            public PlayerPosition position;
            public string type;
            public float sessionTime;
            public Dictionary<string, object> args;
        }
    }

}
