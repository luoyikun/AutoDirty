using Amanda;
using Newtonsoft.Json;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class TestRef : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        ClassRefA classA = new ClassRefA();
        string content = JsonConvert.SerializeObject(classA);
        Debug.Log(content);

        classA.setterClassB.data = 100;
        content = JsonConvert.SerializeObject(classA);
        Debug.Log(content);
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}


public class ClassRefA
{
    public ClassRefB classB = new();

    public ClassRefB setterClassB
    {
        set {
            classB = value;
        }
        get
        {
            if (classB == null)
            {
                classB = new();
            }
            return classB;
        }
    }

    public ClassRefA()
    { 
    
    }

}

public class ClassRefB
{
    public int data = 0;
}