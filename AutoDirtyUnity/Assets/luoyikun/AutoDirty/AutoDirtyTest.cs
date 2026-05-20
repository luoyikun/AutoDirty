using Amanda;
using Newtonsoft.Json;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;

namespace Amanda
{
    public class AutoDirtyTest : MonoBehaviour
    {
        PlayerData m_playerData = new PlayerData();
        public const bool m_isLog = true;

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
                m_playerData.DicQianTao[1] = new Dictionary<int, int>();
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                m_playerData.DicQianTao[1][2] = 3;
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                m_playerData.ListInt[0] = 1;
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                m_playerData.CharData.Data = 2;
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
            else if (Input.GetKeyDown(KeyCode.Alpha8))
            {
                string content = JsonConvert.SerializeObject(m_playerData);
                PlayerData newPlayerData = JsonConvert.DeserializeObject<PlayerData>(content);
                string newContent = JsonConvert.SerializeObject(newPlayerData);
                Debug.Log($"Original Content: {content}");
                Debug.Log($"New Content: {newContent}");
            }
            else if (Input.GetKeyDown(KeyCode.Alpha9))
            {
                IList<ClassC> listClassC = m_playerData.CharData.ListClassC as IList<ClassC>;
                listClassC.Add(new ClassC());
            }
           

                Profiler.BeginSample("TestAutoDirty");
            //m_playerData.CharData.ClassC.Dic[1] = 2;

            Profiler.EndSample();
        }


    }

    [AutoDirty]
    [JsonObject(MemberSerialization.OptIn)]  // 类级别：opt-in 模式
    public partial class PlayerData 
    {
        [JsonProperty]          // 只有打上此标签的成员才会被序列化
        string m_PlayerName;
        [JsonProperty]          // 只有打上此标签的成员才会被序列化
        int m_Level;
        [JsonProperty]          // 只有打上此标签的成员才会被序列化
        List<int> m_ListInt = new();
        [JsonProperty]          // 只有打上此标签的成员才会被序列化
        bool m_bReta;
        [JsonProperty]          // 只有打上此标签的成员才会被序列化
        ChatData m_charData;
        Dictionary<int, Dictionary<int, int>> m_dicQianTao;
        void MarkDirty()
        {
            //SaveManager.Instance.MarkDirty(this);
            if (AutoDirtyTest.m_isLog)
            {
                Debug.Log("PlayerData has been marked dirty.");  
            }
        }
    }

    [AutoDirty]
    [JsonObject(MemberSerialization.OptIn)]  // 类级别：opt-in 模式
    public partial class ChatData 
    {
        [JsonProperty]          // 只有打上此标签的成员才会被序列化
        int m_data;
        [JsonProperty]          // 只有打上此标签的成员才会被序列化
        List<int> m_ListB = new();
        //[AutoDirtyRawCollection]
        [JsonProperty]          // 只有打上此标签的成员才会被序列化
        List<ClassC> m_listClassC = new();
        [JsonProperty]          // 只有打上此标签的成员才会被序列化
        ClassC m_classC = new();

        void MarkDirty()
        {
            if (AutoDirtyTest.m_isLog)
            {
                Debug.Log("ChatData has been marked dirty.");
            }
        }
    }

    [AutoDirty]
    [JsonObject(MemberSerialization.OptIn)]  // 类级别：opt-in 模式
    public partial class ClassC
    {
        [JsonProperty]          // 只有打上此标签的成员才会被序列化
        Dictionary<int, int> m_dic  = new();
        [JsonProperty]          // 只有打上此标签的成员才会被序列化
        HashSet<int> m_hashTest = new();
        void MarkDirty()
        {
            if (AutoDirtyTest.m_isLog)
            {
                Debug.Log("ClassC has been marked dirty.");
            }
        }
    }



}
