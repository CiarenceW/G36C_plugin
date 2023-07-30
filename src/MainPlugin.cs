using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using Receiver2;
using Receiver2ModdingKit;
using Receiver2ModdingKit.CustomSounds;
using UnityEngine;

namespace G36C_plugin
{
    public class G36C_Script : ModGunScript
    {
        private float slide_forward_speed = -8;
        private float hammer_accel = -5000;
        private ModHelpEntry help_entry;
        private int fired_bullet_count;
        private float safety_held_time;
        private readonly float[] slide_push_hammer_curve = new float[] {
            0f,
            0f,
            0.35f,
            1f
        };
        private RotateMover bolt_handle = new();
        private bool handle_grabed;
        private bool has_shot;
        public override void InitializeGun()
        {
            pooled_muzzle_flash = ((GunScript)ReceiverCoreScript.Instance().generic_prefabs.First(it => { return it is GunScript && ((GunScript)it).gun_model == GunModel.m1911; })).pooled_muzzle_flash;

            ReceiverCoreScript.Instance().GetMagazinePrefab("Ciarencew.G36C", MagazineClass.StandardCapacity).glint_renderer.material = ReceiverCoreScript.Instance().GetMagazinePrefab("wolfire.glock_17", MagazineClass.StandardCapacity).glint_renderer.material;
            ReceiverCoreScript.Instance().GetMagazinePrefab("Ciarencew.G36C", MagazineClass.LowCapacity).glint_renderer.material = ReceiverCoreScript.Instance().GetMagazinePrefab("wolfire.glock_17", MagazineClass.StandardCapacity).glint_renderer.material;
        }

        public override void AwakeGun()
        {
            hammer.amount = 1f;

            bolt_handle.rotations[0] = new Quaternion(0f, 0f, 0.0009f, 1f);
            bolt_handle.rotations[1] = new Quaternion(0f, 0f, -0.7071068f, 0.7071068f);

            bolt_handle.transform = transform.Find("slide/bolt_handle");

            var extractor_depressor_plunger = AccessTools.Field(typeof(GunScript), "extractor_depressor_plunger").GetValue(this) as LinearMover;
            var extractor = AccessTools.Field(typeof(GunScript), "extractor").GetValue(this) as LinearRotateMover;

            Debug.Log(transform.Find("slide/bolt/extractor_depressor_plunger") == null);
            extractor_depressor_plunger.transform = transform.Find("slide/bolt/extractor_depressor_plunger");
            if (extractor_depressor_plunger.transform != null)
            {
                extractor_depressor_plunger.positions[0] = extractor_depressor_plunger.transform.localPosition;
                extractor_depressor_plunger.positions[1] = transform.Find("slide/bolt/extractor_depressor_plunger_loaded").localPosition;
                extractor.transform = transform.Find("slide/bolt/extractor");
                extractor.positions[0] = extractor.transform.localPosition;
                extractor.rotations[0] = extractor.transform.localRotation;
                extractor.positions[1] = transform.Find("slide/bolt/extractor_loaded").localPosition;
                extractor.rotations[1] = transform.Find("slide/bolt/extractor_loaded").localRotation;
                Debug.Log(transform.FindDeepChild("extractor_spring").GetComponent<SpringCompressInstance>() == null);
                AccessTools.Field(typeof(GunScript), "extractor_depressor_plunger_spring").SetValue(this, transform.FindDeepChild("extractor_spring").GetComponent<SpringCompressInstance>());
            }
        }

        public override void UpdateGun()
        {
            if (magazine_instance_in_gun != null && magazine_instance_in_gun.extracting)
            {
                gun_animations.keyframes[27] = -0.059f;
                gun_animations.keyframes[29] = -0.0733019f;
            }
            else
            {
                Mathf.Lerp(gun_animations.keyframes[27] = -0.059f, gun_animations.keyframes[27] = -0.056f, Time.deltaTime * 0.0001f);
                Mathf.Lerp(gun_animations.keyframes[29] = -0.0733019f, gun_animations.keyframes[29] = -0.0703019f, Time.deltaTime * 0.0001f);
            }
            if (slide.amount > 0.1f)
            {
                has_shot = false;
            }
            if (player_input.GetButton(RewiredConsts.Action.Pull_Back_Slide))
            {
                bolt_handle.asleep = false;
                bolt_handle.amount = Mathf.MoveTowards(bolt_handle.amount, 1f, Time.deltaTime * 50);
                if (!handle_grabed)
                {
                    ModAudioManager.PlayOneShotAttached("event:/G36/g36_handle_grab", bolt_handle.transform);
                }
                handle_grabed = true;
            }
            else
            {
                bolt_handle.asleep = false;
                bolt_handle.amount = Mathf.MoveTowards(bolt_handle.amount, 0f, Time.deltaTime * 10);
                if (handle_grabed)
                {
                    ModAudioManager.PlayOneShotAttached("event:/G36/g36_handle_release", bolt_handle.transform);
                }
                handle_grabed = false;
            }

            if (_slide_stop_locked && player_input.GetButtonDown(RewiredConsts.Action.Slide_Lock))
            {
                _slide_stop_locked = false;
            }

            if (_select_fire.amount == 0.66f && !_disconnector_needs_reset) //burst fire logic, checks if the select fire component is at the burst fire value, and if the disconnector doesn't need a reset.
            {
                _disconnector_needs_reset = fired_bullet_count >= 3;
            }
            if (slide.amount > 0f && trigger.amount > 0f && (_select_fire.amount < 1f && (_select_fire.amount == 0.66f && fired_bullet_count > 3))) //prevents the gun from going off after racking the slide back on non-auto fire modes when the trigger is held
            {
                Debug.Log(true);
                _disconnector_needs_reset = true;
            }

            hammer.asleep = true;
            hammer.accel = hammer_accel;

            if (slide.amount == 0 && _hammer_state == 3)
            { // Simulate auto sear
                hammer.amount = Mathf.MoveTowards(hammer.amount, _hammer_cocked_val, Time.deltaTime * Time.timeScale * 50);
                if (hammer.amount == _hammer_cocked_val) _hammer_state = 2;
            }
            if (slide.amount > 0 && _hammer_state != 3)
            { // Bolt cocks the hammer when moving back 
                hammer.amount = Mathf.Max(hammer.amount, InterpCurve(slide_push_hammer_curve, slide.amount));
            }

            if (hammer.amount == 1) _hammer_state = 3;

            if (trigger.amount == 0) //checks if the trigger is not being pressed
            {
                fired_bullet_count = 0;
                _disconnector_needs_reset = false;  //if so, mark the disconnector as having been reset.
            }

            if (!IsSafetyOn())
            {
                if (player_input.GetButton(RewiredConsts.Action.Toggle_Safety_Auto_Mod))
                {
                    safety_held_time += Time.deltaTime;
                }
                else
                {
                    safety_held_time = 0;
                }

                if (safety_held_time >= 0.4f)
                {
                    SwitchFireMode();
                }
            }
            else
            {
                safety_held_time = 0;
            }
            if (IsSafetyOn())
            { // Safety blocks the trigger from moving
                trigger.amount = Mathf.Min(trigger.amount, 0.1f);

                trigger.UpdateDisplay();
            }

            if (_hammer_state != 3 && ((trigger.amount == 1 && !_disconnector_needs_reset && slide.amount == 0) || hammer.amount != _hammer_cocked_val))
            { // Move hammer if it's cocked and is free to move
                hammer.asleep = false;
            }

            hammer.TimeStep(Time.deltaTime);

            /*if (player_input.GetButton(RewiredConsts.Action.Toggle_Safety_Auto_Mod)) //safety held logic
            {
				safety_held_down += Time.deltaTime;
				if (safety_held_down > 0.5 && (int)current_firing_mode_index.GetValue(this) != 0) //if the safety key is being held for more than 0.5 sec, forces the safety to be down
                {
					current_firing_mode_index.SetValue(this, 0);
					AudioManager.PlayOneShotAttached(sound_safety_on, this.gameObject);
                }
            }
			else
            {
				safety_held_down = 0;
            }*/

            if (hammer.amount == 0 && _hammer_state == 2)
            { // If hammer dropped and hammer was cocked then fire gun and decock hammer
                TryFireBullet(1, FireBullet);

                if (!dry_fired) has_shot = true;

                _disconnector_needs_reset = _select_fire.amount < 0.66f;

                if (_select_fire.amount == 0.66f)
                {
                    fired_bullet_count++;
                }

                _hammer_state = 0;
            }
            if (slide.vel < 0) slide.vel = Mathf.Max(slide.vel, slide_forward_speed); // Slow down the slide moving forward, reducing fire rate

            hammer.UpdateDisplay();

            trigger.UpdateDisplay();

            bolt_handle.UpdateDisplay();

            var slide_amount_piston = has_shot ? slide.amount : -slide.amount;
            ApplyTransform("slide_stop_cool_anim", slide_stop.amount, transform.Find("slide_stop"));
            ApplyTransform("piston", slide_amount_piston, transform.Find("piston"));
        }

    }
}
