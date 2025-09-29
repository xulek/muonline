using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
using System.Linq;
using System;
using UnityEditor;

public class ObjectLoad : MonoBehaviour
{

    public GameObject[] objects;
 
    void Start()
    {
        string[] reader = System.IO.File.ReadAllLines("C:/Users/Windows-Desktop/Unity/Mu Online/Assets/Resources/Object01/Lorencia.txt");
        for (int i = 0; i < reader.Length; i++)
        {
            char[] delimiters = new char[] { ' ' };
            string[] parts = reader[i].Split(delimiters, StringSplitOptions.RemoveEmptyEntries);
           
            //id
            int id = int.Parse(parts[0]);
            
            //pos
            float posx = float.Parse(parts[1]) ;
            float posy = float.Parse(parts[3]) ;
            float posz = float.Parse(parts[2]) ;

            //rot
            float rotx = float.Parse(parts[4]);
            float roty = 180.0f - float.Parse(parts[6]);
            float rotz = float.Parse(parts[5]);

            //scale
            float scale = float.Parse(parts[7]);

            objects[id].transform.localScale = new Vector3(scale * 100, scale * 100, scale * 100);
            Instantiate(objects[id], new Vector3(posx, posy, posz), Quaternion.Euler(new Vector3(rotx, roty, rotz)));
            
        }
    }
   

}