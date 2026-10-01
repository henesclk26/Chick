using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SmapleChicken {
public class EggHatchScript : MonoBehaviour
{
    private GameObject _Egg;
    private Animator _Anim;
    private float _Time;
    private int _Hatch_Lv = 0;
    void Start()
    {
        _Egg = Instantiate(Resources.Load<GameObject>("Prefabs/egg"),this.transform);
        _Anim = _Egg.GetComponent<Animator>();
    }

    void Update()
    {
        _Time = Time.time;
        if(_Time >= 3 && _Hatch_Lv == 0){
            _Hatch_Lv++;
            _Anim.CrossFade("sway",0.1f, 0, 0.0f);
        }
        else if(_Time >= 6 && _Hatch_Lv == 1){
            _Hatch_Lv++;
            _Anim.CrossFade("hop",0.1f, 0, 0.0f);
        }
        else if(_Time >= 9 && _Hatch_Lv == 2){
            _Hatch_Lv++;
            _Anim.CrossFade("spring_up",0.1f, 0, 0.0f);
        }
        else if(_Time >= 12 && _Hatch_Lv == 3){
            _Hatch_Lv++;
            Destroy(_Egg);
            _Egg = null;
            _Egg = Instantiate(Resources.Load<GameObject>("Prefabs/egg_break"),this.transform);
            _Anim = _Egg.GetComponent<Animator>();
            _Anim.CrossFade("break2",0.0f, 0, 0.0f);
        }
        else if(_Time >= 15 && _Hatch_Lv == 4){
            _Hatch_Lv++;
            Destroy(_Egg);
        }
    }
}
}
