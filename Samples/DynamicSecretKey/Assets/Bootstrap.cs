using Obfuz;
using Obfuz.EncryptionVM;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;


public class Bootstrap : MonoBehaviour
{
    // [ObfuzIgnore]ָʾObfuz��Ҫ�����������
    // ��ʼ��EncryptionService�󱻻����Ĵ�������������У�
    // ��˾����ܵ���س�ʼ������
    [ObfuzIgnore]
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterAssembliesLoaded)]
    private static void SetUpStaticSecretKey()
    {
        Debug.Log("SetUpStaticSecret begin");
        EncryptionService<DefaultStaticEncryptionScope>.Encryptor = new GeneratedEncryptionVirtualMachine(Resources.Load<TextAsset>("Obfuz/defaultStaticSecretKey").bytes);
        Debug.Log("SetUpStaticSecret end");
    }

    private static void SetUpDynamicSecret()
    {
        EncryptionService<DefaultDynamicEncryptionScope>.Encryptor = new GeneratedEncryptionVirtualMachine(Resources.Load<TextAsset>("Obfuz/defaultDynamicSecretKey").bytes);
        // ����������̬EncryptionScope��Encryptor
        // ...
    }


    // Start is called before the first frame update
    void Start()
    {
        // ������ȸ�֮�󣬼����ȸ�DLL֮ǰ������Obfuz�Ķ�̬��Կ
        SetUpDynamicSecret();
#if UNITY_EDITOR
        Assembly ass = AppDomain.CurrentDomain.GetAssemblies().First(ass => ass.GetName().Name == "HotUpdate");
#else
        Assembly ass = Assembly.Load(File.ReadAllBytes($"{Application.streamingAssetsPath}/HotUpdate.dll.bytes"));
#endif
        Type entry = ass.GetType("Entry");
        this.gameObject.AddComponent(entry);
    }
}
