using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;

namespace Compression
{
	public class ArithmeticDecoder
	{
		private static int input_block_size = 32;
		private int input_bit_index_ = input_block_size - 1;
		private UInt32 input_value_;

		private Dictionary<char,UInt64[]> symbol_cdf_map_;
		private UInt64 total_symbols_;
		private UInt64 Lower_, Range_, Value_;

		private UInt64 HALF_MAX = (UInt64)Math.Pow(2,input_block_size-1);
		private UInt64 QUARTER_MAX = (UInt64)Math.Pow(2,input_block_size-2);

		BinaryReader reader_;
		StreamWriter writer_;

		public ArithmeticDecoder (){
			symbol_cdf_map_ = new Dictionary<char, UInt64[]> ();
		}

		public void Decode(string input_filepath,string output_filepath) {
			Stream input_stream = new FileStream(input_filepath,FileMode.Open);
			Stream output_stream = new FileStream(output_filepath, FileMode.OpenOrCreate);
			Decode (input_stream, output_stream);
			input_stream.Close ();
		    output_stream.Close();
		}

		public void Decode(Stream input_stream, Stream output_stream) {
			reader_ = new BinaryReader (input_stream);
			writer_ = new StreamWriter (output_stream);		
			readSymbolCount ();
			readSymbolData ();
			decode ();

		}

		//decode the arithmetically encoded file stream
		private void decode() {
			Range_ = HALF_MAX;
			Lower_ = 0;
			Value_ = reader_.ReadUInt32 ();

			UInt64 cumulative_frequency_of_symbol;
			char symbol = (char)0;

			//decode each symbole
			for (UInt64 i = 0; i < total_symbols_; ++i) {
				cumulative_frequency_of_symbol = (UInt64)(((Value_ - Lower_ + 1) * total_symbols_) - 1) / Range_;
				foreach (KeyValuePair<char,UInt64 []> pair in symbol_cdf_map_) {
					if (cumulative_frequency_of_symbol >= pair.Value [0] && 
					    cumulative_frequency_of_symbol < pair.Value [1]) {
						symbol = pair.Key;
						break;
					}
				}

				rescale_decoding_boundaries(symbol);
				write_symbol (symbol);
			}
			reader_.Close ();
			writer_.Flush ();
			writer_.Close ();
		}

		//write the decoded symbol to file
		private void write_symbol (char symbol) {
			writer_.Write (symbol.ToString());
		}


		//one a character is decoded, the boundaries used for decoding the next
		//symbol must be adjusted
		private void  rescale_decoding_boundaries(char symbol) {
			//get the upper and lower bounds of probabilities
			double l = (double)symbol_cdf_map_ [symbol] [0];
			double r = (double)symbol_cdf_map_ [symbol] [1];

			//adjust to use UINTs vs floating points
			Lower_ += (UInt64)((double)Range_ *(l / (double)total_symbols_));
			Range_ = (UInt64)(Range_* (r/total_symbols_)) - (UInt64)(Range_* (l/total_symbols_));

			while (Range_ <= QUARTER_MAX ) {
				if (Lower_ + Range_ <= HALF_MAX) {
					//don't do anything
				}
				else if (HALF_MAX <= Lower_) {
					Lower_ -= HALF_MAX;
					Value_ -= HALF_MAX;
				} else {
					Lower_ -= QUARTER_MAX;
					Value_ -= QUARTER_MAX;
				}	
				//rescale and get the next bit used in decoding
				Lower_ = 2 * Lower_;
				Range_ = 2 * Range_;
				Value_ = 2 * Value_ + (UInt64)get_next_bit ();
			}
		}

		//read the next bit from the input bit stream
		private UInt64 get_next_bit() {
			UInt64 return_value;
			if(input_bit_index_ == input_block_size -1){
				//return 0 if attemping to run past the end of the bit stream
				//this shouldn't happen but used as a precaution
				if (reader_.BaseStream.Length == reader_.BaseStream.Position)
					return 0;
				input_value_ = reader_.ReadUInt32 ();
			}

			//get the bit specified by input_bit_index_ using boolean operator
			if ((input_value_ & (UInt32)Math.Pow (2, input_bit_index_)) != 0 )
				return_value = 1;
			else
				return_value = 0;

			//decrement bit index
			input_bit_index_ = mod(input_bit_index_ - 1,input_block_size);
			return return_value;
		}

		//read the number of symbols to be decoded from file
		private void readSymbolCount(){
			total_symbols_ = reader_.ReadUInt64 ();
		}

		//read the table in the header of the compressed file which
		//lists the alphabet used and the frequencies of that symbol
		//in this case, each symbol is an ASCII character
	    private void readSymbolData() {
			UInt16 alphabetCount = reader_.ReadUInt16 ();
			List<KeyValuePair<char,UInt64>> char_frequency_pairs;
			char_frequency_pairs = new List<KeyValuePair<char, UInt64>> ();
			KeyValuePair<char, UInt64> char_freq_pair;
			char symbol;
			UInt64 symbolCount;
			UInt64 cumulative_sum = 0;

			for (int i = 0; i < alphabetCount; ++i) {
				symbol = reader_.ReadChar ();
				symbolCount = reader_.ReadUInt64();
				char_freq_pair = new KeyValuePair<char,UInt64> (symbol, symbolCount);
				char_frequency_pairs.Add (char_freq_pair);
			}

			foreach (KeyValuePair<char,UInt64> pair in char_frequency_pairs) {
				symbol_cdf_map_ [pair.Key] = new UInt64[2] { cumulative_sum, cumulative_sum+pair.Value };
				cumulative_sum += pair.Value;
			}
		}

		//mod function to insure that if modulus 
		//function works properly for b < 0
		private int mod(int a, int b) {
			int r = a % b;
			return r < 0 ? r + b : r;
		}
	}
}

