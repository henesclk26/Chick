using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SmapleChicken {
public class HPSystem : MonoBehaviour {

	private int maxHP = 10000;
	private GameObject image;
	private GameObject textObj;
	private Text text;
	private int hp_num = 10000;

	void Start () {
		image = GameObject.Find("HpGauge");
		textObj = GameObject.Find("HpText");
	}
	void Update() {
		if (-1 <= hp_num){
			hp_num--;
		}
		HPDown(hp_num, maxHP);

		if (0 <= hp_num){
			textObj.GetComponent<Text>().text = ((int)hp_num).ToString();
		}
	}
	private void HPDown (float current, int max) {
		image.GetComponent<Image>().fillAmount = current / max;
	}
	public int HP_Public{
		get { return hp_num; }
		set { hp_num = value; }
	}
}
}