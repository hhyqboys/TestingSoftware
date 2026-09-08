// <copyright file=McuModel company="Sumarte Technology">
// Copyright (c) 2024 All Rights Reserved
// </copyright>
// <author>edcer@sumarte.com</author>
// <date>2026/8/26 9:45:01</date>
// <summary></summary>
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TestingSoftware.Models
{
    /// <summary>
    /// The configuration of the motor controller unit.
    /// </summary>
    public class McuModel : FileModel
    {
        #region Clock and ADC Configuration (mc_conversion.h)

        /// <summary>
        /// System clock frequency in Hz (170 MHz).
        /// </summary>
        public long SYSCLK_FREQ { get; set; } = 170000000;

        /// <summary>
        /// Timer clock divider coefficient (no division).
        /// </summary>
        public int TIM_CLOCK_DIVIDER { get; set; } = 1;

        /// <summary>
        /// Advanced timer clock frequency in MHz (170 MHz).
        /// </summary>
        public int ADV_TIM_CLK_MHZ { get; set; } = 170;

        /// <summary>
        /// ADC clock frequency in MHz (42 MHz).
        /// </summary>
        public int ADC_CLK_MHZ { get; set; } = 42;

        /// <summary>
        /// Bus voltage software filter bandwidth factor.
        /// </summary>
        public int VBUS_SW_FILTER_BW_FACTOR { get; set; } = 6;

        /// <summary>
        /// ADC trigger conversion latency in cycles (3.5 cycles).
        /// </summary>
        public double ADC_TRIG_CONV_LATENCY_CYCLES { get; set; } = 3.5;

        /// <summary>
        /// ADC successive approximation register (SAR) conversion cycles (12.5 cycles).
        /// </summary>
        public double ADC_SAR_CYCLES { get; set; } = 12.5;

        #endregion

        #region Current Conversion (mc_conversion.h)

        /// <summary>
        /// ADC reference voltage in volts (3.3 V).
        /// </summary>
        public double ADC_REFERENCE_VOLTAGE { get; set; } = 3.3;

        #endregion

        #region Control Frequencies and Delays (mc_conversion.h)

        /// <summary>
        /// System tick frequency in Hz (0.5 ms period, 2000 Hz).
        /// </summary>
        public ushort SYS_TICK_FREQUENCY { get; set; } = 2000;

        #endregion

        #region PWM Generation and Current Sensing (mc_conversion.h)

        /// <summary>
        /// Minimum high-level time (overwritten at runtime).
        /// </summary>
        public ushort HTMIN { get; set; } = 1;

        /// <summary>
        /// Temperature sensor software filter bandwidth factor (to be implemented).
        /// </summary>
        public uint TEMP_SW_FILTER_BW_FACTOR { get; set; } = 250;

        /// <summary>
        /// ADC sampling cycle correction for STM32G431RBTx (0.5 cycles).
        /// </summary>
        public double SAMPLING_CYCLE_CORRECTION { get; set; } = 0.5;

        #endregion

        #region Encoder Timer (mc_conversion.h)

        /// <summary>
        /// Encoder input capture filter value.
        /// </summary>
        public int ENC_IC_FILTER { get; set; } = 12;

        #endregion

        #region Speed Measurement (mc_driver.h)

        /// <summary>
        /// Number of speed measurement errors before fault detection.
        /// </summary>
        public int SS_MEAS_ERRORS_BEFORE_FAULTS { get; set; } = 3;

        /// <summary>
        /// Position control loop execution frequency in Hz.
        /// </summary>
        public ushort POSITION_LOOP_FREQUENCY_HZ { get; set; } = 1000;

        /// <summary>
        /// Depth of FIFO for averaging mechanical speed (0.1 Hz resolution).
        /// </summary>
        public int ENC_AVRG_FIFO_DEPTH { get; set; } = 16;

        #endregion

        #region PWM Generation and Current Reading (mc_driver.h)

        /// <summary>
        /// PWM frequency in Hz (30 kHz).
        /// </summary>
        public int PWM_FREQUENCY { get; set; } = 30000;

        /// <summary>
        /// PWM frequency scaling factor.
        /// </summary>
        public int PWM_FREQ_SCALING { get; set; } = 1;

        ///// <summary>
        ///// Enable low-side signals for PWM generation.
        ///// </summary>
        //public bool LOW_SIDE_SIGNALS_ENABLING { get; set; } = true;

        /// <summary>
        /// Field-oriented control (FOC) execution rate in PWM cycles.
        /// </summary>
        public int REGULATION_EXECUTION_RATE { get; set; } = 1;

        #endregion

        #region Control Loops (mc_driver.h)

        /// <summary>
        /// Default control mode (speed control).
        /// </summary>
        public string DEFAULT_CONTROL_MODE { get; set; } = "mc_ctrl_mode_speed";

        /// <summary>
        /// Default torque control loop proportional gain.
        /// </summary>
        public Int16 PID_TORQUE_KP_DEFAULT { get; set; } = 252;

        /// <summary>
        /// Default torque control loop integral gain.
        /// </summary>
        public Int16 PID_TORQUE_KI_DEFAULT { get; set; } = 46;

        /// <summary>
        /// Default torque control loop differential gain.
        /// </summary>
        public Int16 PID_TORQUE_KD_DEFAULT { get; set; } = 100;

        /// <summary>
        /// Torque control loop proportional gain divider.
        /// </summary>
        public UInt16 PID_TORQUE_KP_DIV { get; set; } = 8192;

        /// <summary>
        /// Torque control loop integral gain divider.
        /// </summary>
        public UInt16 PID_TORQUE_KI_DIV { get; set; } = 16384;

        /// <summary>
        /// Torque control loop differential gain divider.
        /// </summary>
        public UInt16 PID_TORQUE_KD_DIV { get; set; } = 8192;

        /// <summary>
        /// Log2 of torque proportional gain divider.
        /// </summary>
        public byte PID_TORQUE_KP_DIV_LOG { get; set; } = 13; // log2(8192)

        /// <summary>
        /// Log2 of torque integral gain divider.
        /// </summary>
        public byte PID_TORQUE_KI_DIV_LOG { get; set; } = 14; // log2(16384)

        /// <summary>
        /// Log2 of torque differential gain divider.
        /// </summary>
        public byte PID_TORQUE_KD_DIV_LOG { get; set; } = 13; // log2(8192)

        /// <summary>
        /// Enable differential term for torque control loops (0 = disabled).
        /// </summary>
        public int PID_TORQUE_DIFF_TERM_ENABLING { get; set; } = 0;

        /// <summary>
        /// Default flux control loop proportional gain.
        /// </summary>
        public Int16 PID_FLUX_KP_DEFAULT { get; set; } = 252;

        /// <summary>
        /// Default flux control loop integral gain.
        /// </summary>
        public Int16 PID_FLUX_KI_DEFAULT { get; set; } = 46;

        /// <summary>
        /// Default flux control loop differential gain.
        /// </summary>
        public Int16 PID_FLUX_KD_DEFAULT { get; set; } = 100;

        /// <summary>
        /// Flux control loop proportional gain divider.
        /// </summary>
        public UInt16 PID_FLUX_KP_DIV { get; set; } = 8192;

        /// <summary>
        /// Flux control loop integral gain divider.
        /// </summary>
        public UInt16 PID_FLUX_KI_DIV { get; set; } = 16384;

        /// <summary>
        /// Flux control loop differential gain divider.
        /// </summary>
        public UInt16 PID_FLUX_KD_DIV { get; set; } = 8192;

        /// <summary>
        /// Log2 of flux proportional gain divider.
        /// </summary>
        public byte PID_FLUX_KP_DIV_LOG { get; set; } = 13; // log2(8192)

        /// <summary>
        /// Log2 of flux integral gain divider.
        /// </summary>
        public byte PID_FLUX_KI_DIV_LOG { get; set; } = 14; // log2(16384)

        /// <summary>
        /// Log2 of flux differential gain divider.
        /// </summary>
        public byte PID_FLUX_KD_DIV_LOG { get; set; } = 13; // log2(8192)

        /// <summary>
        /// Enable differential term for flux control loops (0 = disabled).
        /// </summary>
        public int PID_FLUX_DIFF_TERM_ENABLING { get; set; } = 0;

        /// <summary>
        /// Default speed control loop proportional gain (adjusted for 0.1 Hz unit).
        /// </summary>
        public Int16 PID_SPEED_KP_DEFAULT { get; set; } = 2061; // Assuming SPEED_UNIT/10 = 1 for simplicity

        /// <summary>
        /// Default speed control loop integral gain (adjusted for 0.1 Hz unit).
        /// </summary>
        public Int16 PID_SPEED_KI_DEFAULT { get; set; } = 2954; // Assuming SPEED_UNIT/10 = 1 for simplicity

        /// <summary>
        /// Default speed control loop differential gain (adjusted for 0.1 Hz unit).
        /// </summary>
        public Int16 PID_SPEED_KD_DEFAULT { get; set; } = 2954; // Assuming SPEED_UNIT/10 = 1 for simplicity

        /// <summary>
        /// Speed control loop proportional gain divider.
        /// </summary>
        public UInt16 PID_SPEED_KP_DIV { get; set; } = 16;

        /// <summary>
        /// Speed control loop integral gain divider.
        /// </summary>
        public UInt16 PID_SPEED_KI_DIV { get; set; } = 8192;

        /// <summary>
        /// Speed control loop differential gain divider.
        /// </summary>
        public UInt16 PID_SPEED_KD_DIV { get; set; } = 16;

        /// <summary>
        /// Log2 of speed control loop proportional gain divider.
        /// </summary>
        public byte PID_SPEED_KP_DIV_LOG { get; set; } = 4; // log2(16)

        /// <summary>
        /// Log2 of speed control loop integral gain divider.
        /// </summary>
        public byte PID_SPEED_KI_DIV_LOG { get; set; } = 13; // log2(8192)

        /// <summary>
        /// Log2 of speed control loop differential gain divider.
        /// </summary>
        public byte PID_SPEED_KD_DIV_LOG { get; set; } = 4; // log2(16)

        /// <summary>
        /// Enable differential term for speed control loop (0 = disabled).
        /// </summary>
        public int PID_SPEED_DIFF_TERM_ENABLING { get; set; } = 0;

        /// <summary>
        /// Default position control loop proportional gain.
        /// </summary>
        public Int16 PID_POS_KP_DEFAULT { get; set; } = 10000;

        /// <summary>
        /// Default position control loop integral gain.
        /// </summary>
        public Int16 PID_POS_KI_DEFAULT { get; set; } = 1000;

        /// <summary>
        /// Default position control loop differential gain.
        /// </summary>
        public Int16 PID_POS_KD_DEFAULT { get; set; } = 1000;

        /// <summary>
        /// Position control loop proportional gain divider.
        /// </summary>
        public UInt16 PID_POS_KP_DIV { get; set; } = 1024;

        /// <summary>
        /// Position control loop integral gain divider.
        /// </summary>
        public UInt16 PID_POS_KI_DIV { get; set; } = 32768;

        /// <summary>
        /// Position control loop differential gain divider.
        /// </summary>
        public UInt16 PID_POS_KD_DIV { get; set; } = 16;

        /// <summary>
        /// Log2 of position control loop proportional gain divider.
        /// </summary>
        public byte PID_POS_KP_DIV_LOG { get; set; } = 10; // log2(1024)

        /// <summary>
        /// Log2 of position control loop integral gain divider.
        /// </summary>
        public byte PID_POS_KI_DIV_LOG { get; set; } = 15; // log2(32768)

        /// <summary>
        /// Log2 of position control loop differential gain divider.
        /// </summary>
        public byte PID_POS_KD_DIV_LOG { get; set; } = 4; // log2(16)

        /// <summary>
        /// Position control loop angle step in degrees.
        /// </summary>
        public double PID_POS_ANGLE_STEP { get; set; } = 10.0;

        /// <summary>
        /// Position control loop movement duration in milliseconds.
        /// </summary>
        public double PID_POS_MOV_DURATION { get; set; } = 10.0;


        /// <summary>
        /// Default position control loop proportional gain.
        /// </summary>
        public Int16 PID_FWC_KP_DEFAULT { get; set; } = 10000;

        /// <summary>
        /// Default position control loop integral gain.
        /// </summary>
        public Int16 PID_FWC_KI_DEFAULT { get; set; } = 1000;

        /// <summary>
        /// Default position control loop differential gain.
        /// </summary>
        public Int16 PID_FWC_KD_DEFAULT { get; set; } = 1000;

        /// <summary>
        /// Position control loop proportional gain divider.
        /// </summary>
        public UInt16 PID_FWC_KP_DIV { get; set; } = 1024;

        /// <summary>
        /// Position control loop integral gain divider.
        /// </summary>
        public UInt16 PID_FWC_KI_DIV { get; set; } = 32768;

        /// <summary>
        /// Position control loop differential gain divider.
        /// </summary>
        public UInt16 PID_FWC_KD_DIV { get; set; } = 16;


        /// <summary>
        /// Log2 of position control loop proportional gain divider.
        /// </summary>
        public byte PID_FWC_KP_DIV_LOG { get; set; } = 10; // log2(1024)

        /// <summary>
        /// Log2 of position control loop integral gain divider.
        /// </summary>
        public byte PID_FWC_KI_DIV_LOG { get; set; } = 15; // log2(32768)

        /// <summary>
        /// Log2 of position control loop differential gain divider.
        /// </summary>
        public byte PID_FWC_KD_DIV_LOG { get; set; } = 4; // log2(16)

        #endregion

        #region Protection Parameters (mc_driver.h)

        /// <summary>
        /// Overvoltage protection threshold in volts.
        /// </summary>
        public int OV_VOLTAGE_THRESHOLD_V { get; set; } = 15;

        /// <summary>
        /// Undervoltage protection threshold in volts.
        /// </summary>
        public int UD_VOLTAGE_THRESHOLD_V { get; set; } = 9;

        ///// <summary>
        ///// Over-temperature protection threshold (to be implemented).
        ///// </summary>
        //public int OV_TEMPERATURE_THRESHOLD { get; set; } = 0;

        ///// <summary>
        ///// Over-temperature hysteresis (to be implemented).
        ///// </summary>
        //public int OV_TEMPERATURE_HYSTERESIS { get; set; } = 0;

        #endregion

        #region Startup Parameters (mc_driver.h)

        /// <summary>
        /// Encoder alignment duration in milliseconds.
        /// </summary>
        public int ALIGNMENT_DURATION { get; set; } = 700;

        /// <summary>
        /// Switch-over duration in milliseconds.
        /// </summary>
        public int TRANSITION_DURATION { get; set; } = 25;

        #endregion

        #region Power Stage Parameters (power_stage.h)

        /// <summary>
        /// Hardware dead time in nanoseconds (1000 ns).
        /// </summary>
        public int HW_DEAD_TIME_NS { get; set; } = 1000;

        /// <summary>
        /// Bus voltage partitioning factor (4%).
        /// </summary>
        public double VBUS_PARTITIONING_FACTOR { get; set; } = 0.04;

        ///// <summary>
        ///// Enable three shunt resistors for current sensing.
        ///// </summary>
        //public bool THREE_SHUNT { get; set; } = true;

        /// <summary>
        /// Shunt resistor value in ohms (10 mOhm).
        /// </summary>
        public double RSHUNT { get; set; } = 0.01;

        /// <summary>
        /// Current sensing amplification gain (16.5).
        /// </summary>
        public double AMPLIFICATION_GAIN { get; set; } = 16.5;

        /// <summary>
        /// Noise duration in nanoseconds (700 ns).
        /// </summary>
        public int TNOIS_NS { get; set; } = 700;

        /// <summary>
        /// Rise time in nanoseconds (1800 ns).
        /// </summary>
        public int TRISE_NS { get; set; } = 1800;

        #endregion

        /// <summary>
        /// The MCU configuration parameters.
        /// </summary>
        public List<SignalModel> Signals { get; set; } = new List<SignalModel>();

        /// <summary>
        /// The default contructor for the mcu control ultility model.
        /// </summary>
        public McuModel() { }
    }
}
