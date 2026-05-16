using Amanda;
using System.Collections.Generic;
using UnityEngine;

namespace MudGame
{
    public class AutoDirtyTest : MonoBehaviour
    {
        PlayerData m_playerData = new PlayerData();


        private void Start()
        {
            //var data = new PlayerData();
            ////data.PlayerName = "Amanda";
            ////data.Level = 10;
            ////data.ListInt.Add(1);
            //data.CharData = default;
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                m_playerData.CharData.Data = 2;
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                m_playerData.ListInt.Add(3);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                m_playerData.ListInt[0] = 1;
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                m_playerData.CharData.ClassC.Dic[1] = 2;
            }
            else if (Input.GetKeyDown(KeyCode.Alpha5))
            {
                m_playerData.CharData.ClassC.HashTest.Add(2);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha6))
            {
                m_playerData.CharData.ListClassC.Add(new ClassC());
            }
            else if (Input.GetKeyDown(KeyCode.Alpha7))
            {
                m_playerData.CharData.ListClassC[0].HashTest.Add(2);
            }
        }
    }

    [AutoDirty]
    public partial class PlayerData 
    {
        string m_PlayerName;
        int m_Level;
        List<int> m_ListInt = new();
        bool m_bReta;
        ChatData m_charData = new();
        void MarkDirty()
        {
            //SaveManager.Instance.MarkDirty(this);
            Debug.Log("PlayerData has been marked dirty.");
        }
    }

    [AutoDirty]
    public partial class ChatData 
    {
        //[AutoDirtyPropertyName("data")]
        int m_data;
        List<int> m_ListB = new();
        List<ClassC> m_listClassC = new();
        ClassC m_classC = new();
        void MarkDirty()
        {
            Debug.Log("ChatData has been marked dirty.");
        }
    }

    [AutoDirty]
    public partial class ClassC
    {
        Dictionary<int, int> m_dic  = new();
        HashSet<int> m_hashTest  = new();
        void MarkDirty()
        {
            Debug.Log("ClassC has been marked dirty.");
        }
    }

    public interface IAutoDirtyNode
    {
        void MarkDirty();
    }
}
