using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

namespace Compression
{

	public class ArithmeticEncoder
	{

		private static int output_block_size = 32;
		private int output_bit_index_ = output_block_size - 1;
		private UInt32 output_value_;
		private Dictionary<char,UInt64[]> symbol_cdf_map_;
		List<KeyValuePair<char,UInt64>> char_frequency_pairs_;
		private UInt64 total_symbols_;
		private UInt64 Lower_, Range_;
		private UInt64 outstanding_bits_;

		private UInt64 HALF_MAX = (UInt64)Math.Pow(2,output_block_size-1);
		private UInt64 QUARTER_MAX = (UInt64)Math.Pow(2,output_block_size-2);

		BinaryWriter writer_;
		StreamReader reader_;

		private enum BitValue{
			ZERO=0,ONE=1
		};

		public ArithmeticEncoder () {
			symbol_cdf_map_ = new Dictionary<char,UInt64[]> ();
			Lower_ = 0;
			Range_ = HALF_MAX;
		}

		public void Encode(string input_filepath,string output_filepath) {
			Stream input_stream = new FileStream(input_filepath,FileMode.Open);
			Stream output_stream = new FileStream(output_filepath, FileMode.OpenOrCreate);
			Encode (input_stream, output_stream);
			input_stream.Close ();
		    output_stream.Close();
		}

		public void Encode(Stream input_stream, Stream output_stream){
			reader_ = new StreamReader (input_stream);
			writer_ = new BinaryWriter (output_stream);
			findSymbolFrequenies (input_stream);
			//write the number of characters that need decoded to the header
			writeLength ();
			//write the character-frequency key-value pairs as a header
			writeCharacterMetaData ();
			//compress and write data
			encodeAndWrite (input_stream, output_stream);
			reader_.Close ();
		}

		//arithmetically encode the input_stream and write the compressed data
		//to output_stream
		private void encodeAndWrite(Stream input_stream, Stream output_stream) {
			reader_.BaseStream.Position = 0;
			reader_.DiscardBufferedData ();
			char symbol;

			//encode each character (byte)
			while (!reader_.EndOfStream) {
				symbol = (char)reader_.Read();
				encodeSymbol (symbol);
			}

			//add end padding to compression at the end
			symbol = '0';
			for (int i = 0; i < 8; ++i) 
				encodeSymbol(symbol);

			flushOutputStream ();
			writer_.Flush ();
			writer_.Close ();
		}

        //arithmetically encode symbol, using integers vs floating point factions
		//therefore, probabilities are from 0 to MAX_UINT vs 0 to 1
		//since the Range_ value shrinks with each character encoded, whenever it 
		//drops below 25%, the boundaries for encoding a rescaled
		public void encodeSymbol(char symbol) {
			double l = (double)symbol_cdf_map_ [symbol] [0];
			double r = (double)symbol_cdf_map_ [symbol] [1];
			Lower_ += (UInt64)(Range_ *(l / (double)total_symbols_));
			Range_ = (UInt64)(Range_* (r/total_symbols_)) - (UInt64)(Range_* (l/total_symbols_));

			//if Range_ < 25%, rescale Lower_ and Range_
			while ( Range_ <= QUARTER_MAX ) {
				if (Lower_ + Range_ <= HALF_MAX) {
					output_plus_follow (BitValue.ZERO);
				} else if (HALF_MAX <= Lower_) {
					output_plus_follow (BitValue.ONE);
					//shift down Lower_ boundary
					Lower_ -= HALF_MAX;
				} else {
					//can't determine at this moment if encoded bit is
					// a 1 or 0
					outstanding_bits_++;
					Lower_ -= QUARTER_MAX;
				}
				//scale
				Lower_ *= 2;
				Range_ *= 2;
			}
		}

		//when a common prefix bit cannot be exactly determined decoding, keep
		//track of the number of bits skipped, once a bit is written,
		//follow with the opposite bit, 'outstanding_bits_' number of times
		//in a row
		private void output_plus_follow(BitValue bit) {
			output_bit (bit);
			while (outstanding_bits_ > 0) {
				//output opposite valued bit
				if (bit == BitValue.ZERO)
					output_bit (BitValue.ONE);
				else
					output_bit (BitValue.ZERO);
				outstanding_bits_--;
			}
		}

		private void output_bit(BitValue bit) {
			//flip the bit to 1 at the given index specified by 'output_bit_index'
			if(bit == BitValue.ONE)
			  output_value_ |= (UInt32)Math.Pow (2, output_bit_index_);

			//once the 4-bytes have been encoded, write to file
			if (output_bit_index_ == 0) {
				writer_.Write ((UInt32)output_value_);
				output_value_ = 0;
			}

			//decrement bit index (mod function wraps back to MSB when < 0)
 			output_bit_index_ = mod (output_bit_index_ - 1, output_block_size);
		}


		//write any encoded date not written to file
		private void flushOutputStream() {
			if (output_bit_index_ < output_block_size -1)
				writer_.Write (output_value_);
		}


		//write number of symbols encoded to file
		//used during decoding
		private void writeLength() {
			writer_.Write (total_symbols_);
		}

		//write the character-frequency key-value pairs to the compressed file
		//as a header, as this information is needed during decoding
		private void writeCharacterMetaData() {
			writer_.Write ((UInt16)char_frequency_pairs_.Count);
			foreach (KeyValuePair<char,UInt64> pair in char_frequency_pairs_) {
				writer_.Write(pair.Key);
				writer_.Write(pair.Value);
			}
		}

		//count each symbol (in this specific case, ASCII characters)
		//this allows the PMFs (probability mass functions) and CMFs
		//(cumulative mass functions) to be created.  The basic idea
		//of arithmetic coding is that more frequently used characters
		//will have shorter bit encodings than less frequently used ones
	    private void findSymbolFrequenies(Stream input_stream){
			StreamReader reader = new StreamReader (input_stream);
			Dictionary<char,UInt64>	 char_frequencies = new Dictionary<char,UInt64> ();
			int ascii_value;
			char char_value;
			UInt64 cumulative_sum = 0;

			while (!reader.EndOfStream) {
				ascii_value = reader.Read();
				char_value = Convert.ToChar (ascii_value);
				if (char_frequencies.ContainsKey (char_value))
					char_frequencies[char_value] += 1;
				else
					char_frequencies [char_value] = 1;
			}

			char_frequency_pairs_ = new List<KeyValuePair<char,UInt64> >();
			foreach (KeyValuePair<char,UInt64> pair in char_frequencies)
				char_frequency_pairs_.Add (pair);

			char_frequency_pairs_.Sort(
				delegate(KeyValuePair<char,UInt64> firstPair,
			         KeyValuePair<char,UInt64> nextPair) {
				return firstPair.Value.CompareTo(nextPair.Value);
			});
			char_frequency_pairs_.Reverse ();

			foreach (KeyValuePair<char,UInt64> pair in char_frequency_pairs_) {
				symbol_cdf_map_ [pair.Key] = new UInt64[2] { cumulative_sum, cumulative_sum+pair.Value };
				cumulative_sum += pair.Value;
			}
			total_symbols_ = cumulative_sum;

	    }

		//mod function to insure that if modulus 
		//function works properly for b < 0
		private int mod(int a, int b) {
			int r = a % b;
			return r < 0 ? r + b : r;
		}

	}
}

