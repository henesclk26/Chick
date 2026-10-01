using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace SmapleChicken {
public class ChickenAnimatorScript : MonoBehaviour
{
	Animator _Animator;
	private float _Chicken_Speed = 1f;
	private float _Speed;
	private bool isRunning = false;
	private int _HP;
	private HPSystem _HP_Num;
	private GameObject _TextObj;
	private bool _ToStop = false;

	private CharacterController _Ctrl;
	private float _Gravity = 5.0f;
	private Vector3 _MoveDirection = Vector3.zero;

	private GameObject _ViewCamera;

	void Start()
	{
		_Animator = this.GetComponent<Animator>();
		_Ctrl = this.GetComponent<CharacterController>();
		_HP_Num = FindObjectOfType<HPSystem>();
		
		_TextObj = GameObject.Find("AnimationState");

		_ViewCamera = GameObject.Find("Camera");
	}
	void Update()
	{
		STATE_TEXT();
		CAMERA();

		_HP = _HP_Num.HP_Public;
		
		if(_HP<=0)
		{
			if(!_ToStop)
			{
				_Animator.CrossFade("down", 0.1f, 0, 0);
				_Animator.CrossFade("wing_down", 0.1f, 1, 0);
				_ToStop = true;
			}
			else if(_ToStop)
			{
				isRunning = false;
				_Animator.SetBool("to_move", false);
				// recovery
				if (Input.GetKeyDown(KeyCode.E))
				{
					_Animator.SetTrigger("jump");
					_HP_Num.HP_Public = 10000;
					_ToStop = false;
					_MoveDirection.y = 3.0f;
				}
			}
		}

		GRAVITY();
		if(_HP>0 && !_Animator.GetCurrentAnimatorStateInfo(0).IsTag("Stop"))
		{
			if(!_Animator.GetCurrentAnimatorStateInfo(0).IsTag("Action") 
					&& !_Animator.GetCurrentAnimatorStateInfo(0).IsTag("Damage"))
			{
				MOVE();
				JUMP();
				KEY_DOWN2();
			}
			KEY_DOWN();
		}
		KEY_UP();

		if(_Animator.GetCurrentAnimatorStateInfo(0).IsTag("Damage"))
		{
			_Animator.SetBool("during_damage", true);
		}
		else if(!_Animator.GetCurrentAnimatorStateInfo(0).IsTag("Damage"))
		{
			_Animator.SetBool("during_damage", false);
		}
	}
	//--------------------------------------------------------------------- CAMERA
	private void CAMERA ()
	{
		_ViewCamera.transform.position = this.transform.position + new Vector3(0, 1, -3);
	}
	//--------------------------------------------------------------------- GRAVITY
	private void GRAVITY ()
	{
		if (CheckGrounded())
		{
			_Animator.SetBool("to_landing", true);
			if(_MoveDirection.y < -0.5f) _MoveDirection.y = -0.5f;
		}
		else if (!CheckGrounded())
		{
			_Animator.SetBool("to_landing", false);
		}
		if (Input.GetKeyDown(KeyCode.W))
		{
			_Animator.SetTrigger("to_flapping");
		}
		else if(_Animator.GetBool("to_flapping") && _Animator.GetCurrentAnimatorStateInfo(0).IsTag("Basis"))
		{
			_Animator.ResetTrigger("to_flapping");
		}
		if (Input.GetKey(KeyCode.W) && !_Animator.GetCurrentAnimatorStateInfo(0).IsTag("Stop"))
		{
			if(this.transform.position.y < 0.5f)
			{
				_MoveDirection.y = 0.5f;
			}
			else{
				_MoveDirection.y = -0.5f;
			}
		}
		else{
			_MoveDirection.y -= _Gravity * Time.deltaTime;
		}
		_Ctrl.Move(_MoveDirection * Time.deltaTime);
	}
	//--------------------------------------------------------------------- isGrounded
    private bool CheckGrounded()
    {
        if (_Ctrl.isGrounded){
            return true;
        }
        Ray ray = new Ray(this.transform.position + Vector3.up * 0.1f, Vector3.down);
        float range = 0.2f;
        return Physics.Raycast(ray, range);
    }
	//--------------------------------------------------------------------- MOVE
	private void MOVE (){
		//------------------------------------------------------------ Speed
        if(isRunning)
        {
        	_Speed = _Chicken_Speed * 2;
        }
		else {
			_Speed = _Chicken_Speed;
		}
		_Animator.SetFloat("speed", _Speed);

        //------------------------------------------------------------ Foreward
        if (Input.GetKey(KeyCode.UpArrow))
        {
        	_Animator.SetBool("to_move", true);
            // velocity
            if(_Animator.GetCurrentAnimatorStateInfo(0).IsName("move") || !_Ctrl.isGrounded)
            {
                Vector3 velocity = this.transform.rotation * new Vector3(0, 0, _Speed);
                MOVE_XZ(velocity);
                MOVE_RESET();
            }
        }
        
        //------------------------------------------------------------ character rotation
        if (Input.GetKey(KeyCode.RightArrow) && !Input.GetKey(KeyCode.LeftArrow))
        {
            this.transform.Rotate(Vector3.up, 0.5f);
        }
        else if (Input.GetKey(KeyCode.LeftArrow) && !Input.GetKey(KeyCode.RightArrow))
        {
            this.transform.Rotate(Vector3.up, -0.5f);
        }
        if (!Input.GetKey(KeyCode.UpArrow) && !Input.GetKey(KeyCode.DownArrow))
        {
            if (Input.GetKey(KeyCode.RightArrow) && !Input.GetKey(KeyCode.LeftArrow))
            {
            	_Animator.SetBool("to_move", true);
            }
            else if (Input.GetKey(KeyCode.LeftArrow) && !Input.GetKey(KeyCode.RightArrow))
            {
            	_Animator.SetBool("to_move", true);
            }
            // rotate stop
            else if (Input.GetKey(KeyCode.RightArrow) && Input.GetKey(KeyCode.LeftArrow))
            {
            	_Animator.SetBool("to_move", false);
            }
        }
	}
	//--------------------------------------------------------------------- MOVE_SUB
	private void MOVE_XZ (Vector3 velocity)
	{
        _MoveDirection = new Vector3 (velocity.x, _MoveDirection.y, velocity.z);
        _Ctrl.Move(_MoveDirection * Time.deltaTime);
    }
    private void MOVE_RESET()
    {
        _MoveDirection.x = 0;
        _MoveDirection.z = 0;
    }
	//--------------------------------------------------------------------- JUMP
	private void JUMP ()
	{
		if(!_Animator.IsInTransition(0))
		{
			if(!_Animator.GetCurrentAnimatorStateInfo(1).IsName("wing_flapping"))
			{
				if (Input.GetKeyDown(KeyCode.S))
				{
					_Animator.SetTrigger("jump");
					_MoveDirection.y = 3.0f;
				}
			}
		}
	}
	//--------------------------------------------------------------------- KEY_DOWN
	private void KEY_DOWN ()
	{
		// run
		if (Input.GetKeyDown(KeyCode.Z))
		{
			isRunning = true;
		}
		// crouch
		if (!_Animator.GetCurrentAnimatorStateInfo(0).IsName("move"))
		{
			if (Input.GetKey(KeyCode.C))
			{
				_Animator.SetBool("to_crouch", true);
			}
		}
		// damage
		if (Input.GetKeyDown(KeyCode.Q))
		{
			_Animator.SetTrigger("damage");
		}
	}
	//--------------------------------------------------------------------- KEY_DOWN2
	private void KEY_DOWN2 ()
	{
		if (Input.GetKeyDown(KeyCode.X))
		{
			_Animator.SetTrigger("honk");
		}
		if (Input.GetKeyDown(KeyCode.D))
		{
			_Animator.SetTrigger("eat");
		}
		if (Input.GetKeyDown(KeyCode.A) && !_Animator.GetCurrentAnimatorStateInfo(0).IsTag("Jump"))
		{
			_Animator.SetTrigger("peck");
		}
	}
	//--------------------------------------------------------------------- KEY_UP
	private void KEY_UP ()
	{
		// run
		if (Input.GetKeyUp(KeyCode.Z))
		{
			isRunning = false;
		}
		// crouch
		else if (Input.GetKeyUp(KeyCode.C))
		{
			_Animator.SetBool("to_crouch", false);
		}
		// move stop
        else if (Input.GetKeyUp(KeyCode.UpArrow))
        {
        	_Animator.SetBool("to_move", false);
        }
        // rotate stop
        else if (!Input.GetKey(KeyCode.UpArrow) && !Input.GetKey(KeyCode.DownArrow))
        {
        	if (Input.GetKeyUp(KeyCode.RightArrow) || Input.GetKeyUp(KeyCode.LeftArrow))
        	{
        		_Animator.SetBool("to_move", false);
            }
        }
	}
	//--------------------------------------------------------------------- STATE_TEXT
	private void STATE_TEXT ()
	{
		if(_Animator.GetCurrentAnimatorStateInfo(0).IsName("idle")){
			_TextObj.GetComponent<Text>().text = "idle";
		}
		else if(_Animator.GetCurrentAnimatorStateInfo(0).IsName("move")){
			if(_Animator.GetFloat("speed") == 1){
				_TextObj.GetComponent<Text>().text = "walk";
			}
			else if(_Animator.GetFloat("speed") > 1){
				_TextObj.GetComponent<Text>().text = "run";
			}
		}
		else if(_Animator.GetCurrentAnimatorStateInfo(0).IsName("flapping")){
			_TextObj.GetComponent<Text>().text = "flapping";
		}
		else if(_Animator.GetCurrentAnimatorStateInfo(0).IsName("peck_flapping")){
			_TextObj.GetComponent<Text>().text = "peck_flapping";
		}
		else if(_Animator.GetCurrentAnimatorStateInfo(0).IsName("peck")){
			_TextObj.GetComponent<Text>().text = "peck";
		}
		else if(_Animator.GetCurrentAnimatorStateInfo(0).IsName("jump")){
			_TextObj.GetComponent<Text>().text = "jump";
		}
		else if(_Animator.GetCurrentAnimatorStateInfo(0).IsName("crouch")){
			_TextObj.GetComponent<Text>().text = "crouch";
		}
		else if(_Animator.GetCurrentAnimatorStateInfo(0).IsName("eat")){
			_TextObj.GetComponent<Text>().text = "eat";
		}
		else if(_Animator.GetCurrentAnimatorStateInfo(0).IsName("honk")){
			_TextObj.GetComponent<Text>().text = "honk";
		}
		else if(_Animator.GetCurrentAnimatorStateInfo(0).IsName("damage")){
			_TextObj.GetComponent<Text>().text = "damage";
		}
		else if(_Animator.GetCurrentAnimatorStateInfo(0).IsName("damage_flapping")){
			_TextObj.GetComponent<Text>().text = "damage_flapping";
		}
		else if(_Animator.GetCurrentAnimatorStateInfo(0).IsName("down")){
			_TextObj.GetComponent<Text>().text = "down";
		}
		else if(_Animator.GetCurrentAnimatorStateInfo(0).IsName("recovery")){
			_TextObj.GetComponent<Text>().text = "recovery";
		}
	}
}
}


