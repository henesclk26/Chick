using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace SmapleChicken {
public class ChickAnimatorScript : MonoBehaviour
{
    // chick
    private Animator animator;
    private CharacterController ctrl;
    // chicken
    private GameObject chicken;
    private Animator chicken_animator;
    private CharacterController chicken_ctrl;
    // move
    private Vector3 moveDirection = Vector3.zero;
    private float gravity = 5.0f;
    // down
    private bool to_stop = false;

    void Start()
    {
        animator = this.GetComponent<Animator>();
        chicken = GameObject.Find("Stage/chicken_lowpoly");
        chicken_animator = chicken.GetComponent<Animator>();
        ctrl = this.GetComponent<CharacterController>();
        chicken_ctrl = chicken.GetComponent<CharacterController>();
    }

    void Update()
    {
        DOWN_AND_RECOVER();
        GRAVITY();
        if(!chicken_animator.GetCurrentAnimatorStateInfo(0).IsTag("Stop"))
		{    
            SET_BOOL();
            if(!animator.GetCurrentAnimatorStateInfo(0).IsTag("Stop"))
		    {
    			if(!animator.GetCurrentAnimatorStateInfo(0).IsTag("Action") 
	    				&& !animator.GetCurrentAnimatorStateInfo(0).IsTag("Damage"))
		    	{
			    	MOVE();
				    JUMP();
    				KEY_DOWN2();
	    		}
		    	KEY_DOWN();
		    }
        }
    }
    //--------------------------------------------------------------------- Gravity
	private void GRAVITY ()
	{
		if (CheckGrounded())
		{
			animator.SetBool("to_landing", true);
			if(moveDirection.y < -0.5f) moveDirection.y = -0.5f;
		}
		else if (!CheckGrounded())
		{
			animator.SetBool("to_landing", false);
		}
        if (Input.GetKey(KeyCode.W) && !animator.GetCurrentAnimatorStateInfo(0).IsTag("Stop"))
		{
			if(this.transform.position.y < 0.5f)
			{
				moveDirection.y = 0.5f;
			}
			else{
				moveDirection.y = 0;
			}
		}
		else{
			moveDirection.y -= gravity * Time.deltaTime;
		}
        ctrl.Move(moveDirection * Time.deltaTime);
    }
    //--------------------------------------------------------------------- isGrounded
    private bool CheckGrounded()
    {
        if (ctrl.isGrounded){
            return true;
        }
        Ray ray = new Ray(this.transform.position + Vector3.up * 0.1f, Vector3.down);
        float range = 0.2f;
        return Physics.Raycast(ray, range);
    }
    //--------------------------------------------------------------------- Move
    private void MOVE ()
    {
        float distance = Vector3.Distance(this.transform.position, chicken.transform.position);
        animator.SetFloat("speed",distance * 2);
        // Look
        var aim = chicken.transform.position - this.transform.position;
        var look = Quaternion.LookRotation(aim, Vector3.up);
        this.transform.localRotation = look;

        if(distance >= 0.5f)
        {
            Vector3 offset = chicken.transform.rotation * new Vector3(0, 0, -0.5f);
            float speed= 2.0f;
            this.transform.position = Vector3.Lerp(this.transform.position, chicken.transform.position + offset, speed * Time.deltaTime);
        }

        if(distance > 0.51f)
        {
            animator.SetBool("to_move", true);
        }
        else
        {
            animator.SetBool("to_move", false);
        }
    }
    //--------------------------------------------------------------------- Jump
    private void JUMP ()
	{
		if(!animator.IsInTransition(0))
		{
			if(!animator.GetCurrentAnimatorStateInfo(1).IsName("wing_flapping"))
			{
				if (Input.GetKeyDown(KeyCode.S))
				{
					animator.SetTrigger("jump");
					moveDirection.y = 3.0f;
				}
			}
		}
	}
    //--------------------------------------------------------------------- Key Down
    private void KEY_DOWN ()
	{
        if (Input.GetKeyDown(KeyCode.Q))
		{
			animator.SetTrigger("damage");
		}
    }
    //--------------------------------------------------------------------- Key Down2
	private void KEY_DOWN2 ()
	{
		if (Input.GetKeyDown(KeyCode.D))
		{
			animator.SetTrigger("eat");
		}
	}
    //--------------------------------------------------------------------- Set Bool
    private void SET_BOOL ()
    {
        // crouch
        if(chicken_animator.GetBool("to_crouch"))
        {
            animator.SetBool("to_crouch", true);
        }
        else if(!chicken_animator.GetBool("to_crouch"))
        {
            animator.SetBool("to_crouch", false);
        }
        // peep
        if(chicken_animator.GetCurrentAnimatorStateInfo(0).IsName("honk"))
        {
            animator.SetBool("peep", true);
        }
        else if(!chicken_animator.GetCurrentAnimatorStateInfo(0).IsName("honk"))
        {
            animator.SetBool("peep", false);
        }
        // peck
        if(chicken_animator.GetCurrentAnimatorStateInfo(0).IsName("peck")
            || chicken_animator.GetCurrentAnimatorStateInfo(0).IsName("peck_flapping"))
        {
            animator.SetBool("peck", true);
        }
        else if(!chicken_animator.GetCurrentAnimatorStateInfo(0).IsName("peck")
            && !chicken_animator.GetCurrentAnimatorStateInfo(0).IsName("peck_flapping"))
        {
            animator.SetBool("peck", false);
        }
        // Damage
        if(animator.GetCurrentAnimatorStateInfo(0).IsTag("Damage"))
		{
			animator.SetBool("during_damage", true);
		}
		else if(!animator.GetCurrentAnimatorStateInfo(0).IsTag("Damage"))
		{
			animator.SetBool("during_damage", false);
		}
    }
    //--------------------------------------------------------------------- Down and Recover
    private void DOWN_AND_RECOVER ()
    {
        if(!to_stop)
		{
            if(chicken_animator.GetCurrentAnimatorStateInfo(0).IsTag("Stop"))
            {
                animator.CrossFade("down", 0.1f, 0, 0);
				animator.CrossFade("wing_down", 0.1f, 1, 0);
                to_stop = true;
            }
        }
        else if(to_stop)
		{
            if(!chicken_animator.GetCurrentAnimatorStateInfo(0).IsTag("Stop"))
            {
                animator.SetTrigger("jump");
                moveDirection.y = 3.0f;
                to_stop = false;
            }
        }
    }
}
}